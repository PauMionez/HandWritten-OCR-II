"""Kraken HTR Server v2 - full-page segmentation with region filtering and caches."""
import os, sys, json, base64, hashlib, logging, tempfile, traceback
from pathlib import Path
from datetime import datetime
from flask import Flask, request, jsonify
from flask_cors import CORS
from PIL import Image

PORT       = 5001
HOST       = "127.0.0.1"
_SCRIPT_DIR = Path(__file__).resolve().parent
MODELS_DIR  = _SCRIPT_DIR.parent / "models" / "KrakenOcr"
LOGS_DIR    = _SCRIPT_DIR.parent / "logs"
LOGS_DIR.mkdir(parents=True, exist_ok=True)

log_file = LOGS_DIR / ("kraken_" + datetime.now().strftime("%Y%m%d") + ".log")
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    handlers=[
        logging.FileHandler(log_file, encoding="utf-8"),
        logging.StreamHandler(sys.stdout),
    ],
)
logger = logging.getLogger(__name__)

app = Flask(__name__)
CORS(app)

_MODEL_CACHE        = {}
_SEGMENTATION_CACHE = {}

def get_available_models():
    if not MODELS_DIR.exists():
        return []
    return [{"name": f.stem, "path": str(f), "size_mb": round(f.stat().st_size / (1024*1024), 2)}
            for f in MODELS_DIR.rglob("*.mlmodel")]

def resolve_model_path(requested):
    if requested:
        p = Path(requested)
        if p.exists(): return str(p)
        c = MODELS_DIR / (requested + ".mlmodel")
        if c.exists(): return str(c)
        c = MODELS_DIR / requested
        if c.exists(): return str(c)
    models = get_available_models()
    return models[0]["path"] if models else None

def md5_of_file(path):
    with open(path, "rb") as f:
        return hashlib.md5(f.read()).hexdigest()

def line_in_region(line, rx, ry, rw, rh):
    for x, y in line.baseline:
        if rx <= x <= rx + rw and ry <= y <= ry + rh:
            return True
    return False

def run_kraken_ocr(image_path, model_path, region=None):
    try:
        from kraken import blla, rpred
        from kraken.lib import models as kraken_models
        from kraken.containers import Segmentation

        if model_path not in _MODEL_CACHE:
            logger.info("Loading model: " + model_path)
            _MODEL_CACHE[model_path] = kraken_models.load_any(model_path)
        else:
            logger.info("Using cached model: " + model_path)
        model = _MODEL_CACHE[model_path]

        logger.info("Opening image: " + image_path)
        img = Image.open(image_path).convert("RGB")
        w, h = img.size

        # Binarize for recognition — nlbin normalises uneven lighting and yellowing.
        # Keep original RGB for blla.segment (neural baseline detector benefits from colour).
        from kraken.binarization import nlbin
        logger.info("Binarizing image for recognition...")
        bin_img = nlbin(img)

        img_hash = md5_of_file(image_path)
        if img_hash not in _SEGMENTATION_CACHE:
            logger.info("Running baseline segmentation on %dx%d ..." % (w, h))
            _SEGMENTATION_CACHE[img_hash] = blla.segment(img)
            logger.info("Segmentation cached (%d lines)" % len(_SEGMENTATION_CACHE[img_hash].lines))
        else:
            logger.info("Using cached segmentation (%d lines)" % len(_SEGMENTATION_CACHE[img_hash].lines))
        baseline_seg = _SEGMENTATION_CACHE[img_hash]

        if region:
            rx, ry, rw, rh = region["x"], region["y"], region["w"], region["h"]
            filtered = [l for l in baseline_seg.lines if line_in_region(l, rx, ry, rw, rh)]
            logger.info("Region (%d,%d %dx%d): %d/%d lines" % (rx, ry, rw, rh, len(filtered), len(baseline_seg.lines)))
            if not filtered:
                logger.warning("No lines found in region")
                return {"success": True, "text": "", "lines": [], "line_count": 0,
                        "model_used": Path(model_path).stem, "error": None}
            baseline_seg = Segmentation(
                type=baseline_seg.type,
                imagename=baseline_seg.imagename,
                text_direction=baseline_seg.text_direction,
                script_detection=baseline_seg.script_detection,
                lines=filtered,
                regions=baseline_seg.regions,
                line_orders=[],
            )

        logger.info("Running recognition...")
        lines, full_text_parts = [], []
        for record in rpred.rpred(model, bin_img, baseline_seg):
            line_text = record.prediction
            lines.append({"text": line_text, "confidence": 0.0})
            if line_text.strip():
                full_text_parts.append(line_text)

        full_text = "\n".join(full_text_parts)
        logger.info("OCR complete. Lines: %d, Characters: %d" % (len(lines), len(full_text)))
        return {"success": True, "text": full_text, "lines": lines, "line_count": len(lines),
                "model_used": Path(model_path).stem, "error": None}

    except Exception as ex:
        logger.error("OCR failed: " + str(ex) + "\n" + traceback.format_exc())
        return {"success": False, "text": "", "lines": [], "line_count": 0, "model_used": "", "error": str(ex)}


@app.route("/health")
def health():
    models = get_available_models()
    return jsonify({"status": "ok", "server": "Kraken HTR v2", "port": PORT,
                    "models_available": len(models),
                    "models": [m["name"] for m in models]})

@app.route("/models")
def list_models():
    return jsonify({"success": True, "models": get_available_models()})

@app.route("/ocr/base64", methods=["POST"])
def ocr_base64():
    data = request.get_json(force=True)
    if not data or "image_base64" not in data:
        return jsonify({"success": False, "error": "Missing image_base64"}), 400
    model_path = resolve_model_path(data.get("model"))
    if not model_path:
        return jsonify({"success": False, "error": "No model found in " + str(MODELS_DIR)}), 400
    region = data.get("region")
    try:
        img_bytes = base64.b64decode(data["image_base64"])
        with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as tmp:
            tmp.write(img_bytes)
            tmp_path = tmp.name
    except Exception as ex:
        return jsonify({"success": False, "error": "Decode error: " + str(ex)}), 400
    try:
        return jsonify(run_kraken_ocr(tmp_path, model_path, region=region))
    finally:
        try: os.unlink(tmp_path)
        except: pass

if __name__ == "__main__":
    logger.info("=" * 55)
    logger.info("  Kraken HTR Server v2  http://%s:%d" % (HOST, PORT))
    logger.info("  Models : " + str(MODELS_DIR))
    logger.info("  Log    : " + str(log_file))
    for m in get_available_models():
        logger.info("    - %s (%.1f MB)" % (m["name"], m["size_mb"]))
    logger.info("=" * 55)
    app.run(host=HOST, port=PORT, debug=False, threaded=True)