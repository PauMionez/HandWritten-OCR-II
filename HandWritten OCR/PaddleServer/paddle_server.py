"""PaddleOCR 3.x Server — lightweight OCR for low-spec hardware."""
import os, sys, base64, logging, tempfile, traceback

# ── Redirect PaddleX model cache to D drive before any paddle import ──────────
from pathlib import Path
_SCRIPT_DIR  = Path(__file__).resolve().parent
MODELS_DIR   = _SCRIPT_DIR.parent / "models" / "PaddleOcr"
MODELS_DIR.mkdir(parents=True, exist_ok=True)
os.environ["PADDLEX_HOME"] = str(MODELS_DIR)   # keeps everything on D drive

# ── Logging setup before Flask/PaddleOCR imports so crashes are recorded ──────
from datetime import datetime
LOGS_DIR = _SCRIPT_DIR.parent / "logs"
LOGS_DIR.mkdir(parents=True, exist_ok=True)
log_file = LOGS_DIR / ("paddle_" + datetime.now().strftime("%Y%m%d") + ".log")
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    handlers=[
        logging.FileHandler(log_file, encoding="utf-8"),
        logging.StreamHandler(sys.stdout),
    ],
)
logger = logging.getLogger(__name__)
logger.info("PaddleOCR server starting — models dir: %s" % MODELS_DIR)

# ── Now safe to import heavy dependencies ─────────────────────────────────────
from flask import Flask, request, jsonify
from flask_cors import CORS
from PIL import Image

PORT = 5002
HOST = "127.0.0.1"

app = Flask(__name__)
CORS(app)

_OCR_CACHE = {}  # lang -> PaddleOCR instance

SUPPORTED_LANGS = {
    "en":     "English",
    "ch":     "Chinese (Simplified)",
    "french": "French",
    "german": "German",
    "japan":  "Japanese",
    "korean": "Korean",
}

def get_ocr(lang="en"):
    if lang not in _OCR_CACHE:
        from paddleocr import PaddleOCR
        logger.info("Loading PaddleOCR for lang=%s (models auto-download on first run)..." % lang)
        _OCR_CACHE[lang] = PaddleOCR(
            lang=lang,
            use_doc_orientation_classify=False,  # skip heavy orientation step
            use_doc_unwarping=False,              # skip unwarping
            use_textline_orientation=False,       # skip angle classifier
            device="cpu",
            cpu_threads=2,                        # low-spec friendly
            enable_mkldnn=False,                  # avoids oneDNN crash on some CPUs
        )
        logger.info("PaddleOCR ready for lang=%s" % lang)
    return _OCR_CACHE[lang]

def parse_result(result):
    """Extract lines from PaddleOCR 3.x predict() result."""
    lines = []
    if not result:
        return lines
    # result is a list of page dicts; we always send one image at a time
    page = result[0] if isinstance(result, list) else result
    texts  = page.get("rec_texts",  []) if isinstance(page, dict) else []
    scores = page.get("rec_scores", []) if isinstance(page, dict) else []
    for text, score in zip(texts, scores):
        if str(text).strip():
            lines.append({"text": str(text), "confidence": round(float(score), 4)})
    return lines

def run_paddle_ocr(image_path, lang="en", region=None):
    try:
        img = Image.open(image_path).convert("RGB")

        if region:
            rx, ry, rw, rh = region["x"], region["y"], region["w"], region["h"]
            img = img.crop((rx, ry, rx + rw, ry + rh))
            logger.info("Cropped to region (%d,%d %dx%d)" % (rx, ry, rw, rh))

        with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as tmp:
            img.save(tmp.name)
            crop_path = tmp.name

        try:
            ocr    = get_ocr(lang)
            result = ocr.predict(crop_path)
        finally:
            try: os.unlink(crop_path)
            except: pass

        lines     = parse_result(result)
        full_text = "\n".join(l["text"] for l in lines)
        logger.info("OCR done. Lines: %d, chars: %d" % (len(lines), len(full_text)))
        return {
            "success":    True,
            "text":       full_text,
            "lines":      lines,
            "line_count": len(lines),
            "lang_used":  lang,
            "error":      None,
        }

    except Exception as ex:
        logger.error("OCR failed: %s\n%s" % (str(ex), traceback.format_exc()))
        return {"success": False, "text": "", "lines": [], "line_count": 0,
                "lang_used": lang, "error": str(ex)}


@app.route("/health")
def health():
    return jsonify({
        "status":          "ok",
        "server":          "PaddleOCR v3",
        "port":            PORT,
        "models_dir":      str(MODELS_DIR),
        "supported_langs": list(SUPPORTED_LANGS.keys()),
        "loaded_langs":    list(_OCR_CACHE.keys()),
    })

@app.route("/ocr/base64", methods=["POST"])
def ocr_base64():
    data = request.get_json(force=True)
    if not data or "image_base64" not in data:
        return jsonify({"success": False, "error": "Missing image_base64"}), 400

    lang   = data.get("lang", "en")
    region = data.get("region")
    if lang not in SUPPORTED_LANGS:
        lang = "en"

    try:
        img_bytes = base64.b64decode(data["image_base64"])
        with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as tmp:
            tmp.write(img_bytes)
            tmp_path = tmp.name
    except Exception as ex:
        return jsonify({"success": False, "error": "Decode error: " + str(ex)}), 400

    try:
        return jsonify(run_paddle_ocr(tmp_path, lang=lang, region=region))
    finally:
        try: os.unlink(tmp_path)
        except: pass


if __name__ == "__main__":
    logger.info("=" * 55)
    logger.info("  PaddleOCR 3.x Server  http://%s:%d" % (HOST, PORT))
    logger.info("  Models: %s" % MODELS_DIR)
    logger.info("  Langs : %s" % ", ".join(SUPPORTED_LANGS.keys()))
    logger.info("=" * 55)
    app.run(host=HOST, port=PORT, debug=False, threaded=True)
