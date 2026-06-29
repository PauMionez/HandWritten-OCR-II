"""PaddleOCR 3.x Server — PP-OCRv6 via paddleocr package (port 5002)."""
import os, sys, base64, logging, tempfile, traceback
from pathlib import Path
from datetime import datetime
from PIL import Image

# ── Paths ─────────────────────────────────────────────────────────────────────
_SCRIPT_DIR = Path(__file__).resolve().parent

# ── Logging ───────────────────────────────────────────────────────────────────
_LOGS_DIR = _SCRIPT_DIR.parent / "logs"
_LOGS_DIR.mkdir(parents=True, exist_ok=True)
_log_file  = _LOGS_DIR / ("paddle_" + datetime.now().strftime("%Y%m%d") + ".log")
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    handlers=[
        logging.FileHandler(_log_file, encoding="utf-8"),
        logging.StreamHandler(sys.stdout),
    ],
)
logger = logging.getLogger(__name__)
logger.info("PaddleOCR server starting — models dir: %s", _MODELS_DIR)

# ── Flask ─────────────────────────────────────────────────────────────────────
from flask import Flask, request, jsonify
from flask_cors import CORS

PORT = 5002
HOST = "127.0.0.1"
app  = Flask(__name__)
CORS(app)

_OCR_CACHE = {}  # lang -> PaddleOCR instance

SUPPORTED_LANGS = {
    "en":     "English",
    "ch":     "Chinese (Simplified)",
    "french": "French",
    "german": "German",
}


def get_ocr(lang="en"):
    if lang not in _OCR_CACHE:
        from paddleocr import PaddleOCR
        logger.info("Loading PaddleOCR for lang=%s...", lang)
        _OCR_CACHE[lang] = PaddleOCR(
            lang=lang,
            device="cpu",
            cpu_threads=2,
            enable_mkldnn=False,
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
        )
        logger.info("PaddleOCR ready for lang=%s", lang)
    return _OCR_CACHE[lang]


def _parse(result):
    """Extract lines from PaddleOCR 3.x predict() result."""
    lines = []
    if not result:
        return lines
    page = result[0] if isinstance(result, list) else result
    texts  = page.get("rec_texts",  []) if isinstance(page, dict) else []
    scores = page.get("rec_scores", []) if isinstance(page, dict) else []
    for text, score in zip(texts, scores):
        if str(text).strip():
            lines.append({"text": str(text), "confidence": round(float(score), 4)})
    return lines


def run_ocr(image_path, lang="en", region=None):
    try:
        img = Image.open(image_path).convert("RGB")

        if region and isinstance(region, dict):
            rx = int(float(region.get("x", 0)))
            ry = int(float(region.get("y", 0)))
            rw = int(float(region.get("w", 0)))
            rh = int(float(region.get("h", 0)))
            if rw > 0 and rh > 0:
                img = img.crop((rx, ry, rx + rw, ry + rh))
                logger.info("Cropped to region (%d,%d %dx%d)", rx, ry, rw, rh)

        with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as tmp:
            img.save(tmp.name)
            tmp_path = tmp.name

        try:
            result = list(get_ocr(lang).predict(tmp_path))
        finally:
            try: os.unlink(tmp_path)
            except: pass

        lines     = _parse(result)
        full_text = "\n".join(l["text"] for l in lines)
        logger.info("OCR done — %d lines, %d chars", len(lines), len(full_text))
        return {"success": True, "text": full_text, "lines": lines,
                "line_count": len(lines), "lang_used": lang, "error": None}

    except Exception as ex:
        logger.error("OCR failed: %s\n%s", ex, traceback.format_exc())
        return {"success": False, "text": "", "lines": [], "line_count": 0,
                "lang_used": lang, "error": str(ex)}


@app.route("/health")
def health():
    return jsonify({"status": "ok", "port": PORT,
                    "loaded_langs": list(_OCR_CACHE.keys())})


@app.route("/ocr/base64", methods=["POST"])
def ocr_base64():
    data = request.get_json(force=True)
    if not data or "image_base64" not in data:
        return jsonify({"success": False, "error": "Missing image_base64"}), 400

    lang = data.get("lang", "en")
    if lang not in SUPPORTED_LANGS:
        lang = "en"

    try:
        raw = base64.b64decode(data["image_base64"])
        with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as tmp:
            tmp.write(raw); tmp_path = tmp.name
    except Exception as ex:
        return jsonify({"success": False, "error": "Decode error: " + str(ex)}), 400

    try:
        return jsonify(run_ocr(tmp_path, lang=lang, region=data.get("region")))
    finally:
        try: os.unlink(tmp_path)
        except: pass


if __name__ == "__main__":
    logger.info("=" * 55)
    logger.info("  PaddleOCR 3.x Server  http://%s:%d", HOST, PORT)
    logger.info("  Models: %s", _MODELS_DIR)
    logger.info("  Langs : %s", ", ".join(SUPPORTED_LANGS.keys()))
    logger.info("=" * 55)
    app.run(host=HOST, port=PORT, debug=False, threaded=True)
