"""PaddleOCR Server — PP-OCRv6 via paddleocr package (port 5002)."""
import os, sys, base64, logging, tempfile, traceback
from pathlib import Path
from datetime import datetime
from PIL import Image

# ── Paths ─────────────────────────────────────────────────────────────────────
_SCRIPT_DIR = Path(__file__).resolve().parent
_MODELS_DIR = (_SCRIPT_DIR if _SCRIPT_DIR.name.lower() == "paddleocr"
               else _SCRIPT_DIR.parent / "models" / "PaddleOcr")
_MODELS_DIR.mkdir(parents=True, exist_ok=True)

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

_ocr = None  # loaded on first OCR request


def _patch_mkldnn():
    """Disable oneDNN before paddle loads — fixes PIR crash on Windows CPU."""
    try:
        import paddle.inference as _pi
        import paddle as _pd
        _Orig = _pi.Config
        def _no_mkldnn(*a, **kw):
            c = _Orig(*a, **kw)
            c.disable_mkldnn()
            return c
        _pi.Config = _no_mkldnn
        _pd.inference.Config = _no_mkldnn
        logger.info("MKLDNN patched.")
    except Exception as e:
        logger.warning("MKLDNN patch skipped: %s", e)


def get_ocr():
    global _ocr
    if _ocr is None:
        _patch_mkldnn()
        from paddleocr import PaddleOCR
        logger.info("Loading PaddleOCR engine...")
        _ocr = PaddleOCR(
            lang="en",
            device="cpu",
        )
        logger.info("PaddleOCR engine ready.")
    return _ocr


def _safe_str(v):
    return str(v).strip() if v is not None else ""

def _safe_float(v):
    try:    return float(v)
    except: return 0.0


def _parse(result):
    """Parse paddleocr 3.x result — list of OCRResult with rec_texts/rec_scores."""
    lines = []
    if result is None:
        return lines
    logger.info("Result type: %s  len: %s", type(result).__name__, len(result) if hasattr(result, '__len__') else '?')
    for res in result:
        if res is None:
            continue
        logger.info("  res type: %s", type(res).__name__)
        # paddleocr 3.x / paddlex: OCRResult with rec_texts / rec_scores attributes
        if isinstance(res, dict):
            rec_texts  = res.get("rec_texts")
            rec_scores = res.get("rec_scores")
        else:
            rec_texts  = getattr(res, "rec_texts",  None)
            rec_scores = getattr(res, "rec_scores", None)

        if rec_texts is not None:
            logger.info("  rec_texts count: %d", len(rec_texts))
            if rec_scores is None:
                rec_scores = [0.0] * len(rec_texts)
            for t, s in zip(rec_texts, rec_scores):
                text = _safe_str(t)
                if text:
                    lines.append({"text": text, "confidence": round(_safe_float(s), 4)})
            continue

        # boxes layout
        boxes = res.get("boxes") if isinstance(res, dict) else getattr(res, "boxes", None)
        if boxes is not None:
            logger.info("  boxes count: %d", len(boxes))
            for box in boxes:
                if isinstance(box, dict):
                    text  = _safe_str(box.get("rec_text"))
                    score = _safe_float(box.get("rec_score", 0))
                else:
                    text  = _safe_str(getattr(box, "rec_text",  None))
                    score = _safe_float(getattr(box, "rec_score", 0))
                if text:
                    lines.append({"text": text, "confidence": round(score, 4)})
            continue

        logger.info("  unrecognised result shape — attrs: %s", [a for a in dir(res) if not a.startswith('_')])
    return lines


def run_ocr(image_path, region=None):
    try:
        img = Image.open(image_path).convert("RGB")

        if region is not None and isinstance(region, dict):
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
            result = get_ocr().ocr(tmp_path)
        finally:
            try: os.unlink(tmp_path)
            except: pass

        lines     = _parse(result)
        full_text = "\n".join(l["text"] for l in lines)
        logger.info("OCR done — %d lines, %d chars", len(lines), len(full_text))
        return {"success": True, "text": full_text, "lines": lines,
                "line_count": len(lines), "lang_used": "en", "error": None}

    except Exception as ex:
        logger.error("OCR failed: %s\n%s", ex, traceback.format_exc())
        return {"success": False, "text": "", "lines": [], "line_count": 0,
                "lang_used": "en", "error": str(ex)}


@app.route("/health")
def health():
    return jsonify({"status": "ok", "port": PORT,
                    "engine_ready": _ocr is not None})


@app.route("/ocr/base64", methods=["POST"])
def ocr_base64():
    data = request.get_json(force=True)
    if not data or "image_base64" not in data:
        return jsonify({"success": False, "error": "Missing image_base64"}), 400

    try:
        raw = base64.b64decode(data["image_base64"])
        with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as tmp:
            tmp.write(raw); tmp_path = tmp.name
    except Exception as ex:
        return jsonify({"success": False, "error": "Decode error: " + str(ex)}), 400

    try:
        return jsonify(run_ocr(tmp_path, region=data.get("region")))
    finally:
        try: os.unlink(tmp_path)
        except: pass


if __name__ == "__main__":
    logger.info("=" * 55)
    logger.info("  PaddleOCR Server  http://%s:%d", HOST, PORT)
    logger.info("  paddleocr package  |  engine loads on first request")
    logger.info("=" * 55)
    app.run(host=HOST, port=PORT, debug=False, threaded=True)
