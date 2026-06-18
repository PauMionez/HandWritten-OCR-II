# HandWritten OCR

A WPF desktop application that recognizes handwritten text from images
using Microsoft TrOCR and Kraken HTR — running locally with no internet, no API, no cloud.

---

## Preview

> Drop a handwritten image → Click Run OCR → Get recognized text instantly

---

## Features

- Handwritten text recognition via **TrOCR** (English modern handwriting)
- Handwritten text recognition via **Kraken HTR** (historical manuscripts — Latin, Italian, French, Spanish and more)
- Region drawing — mark specific areas on the image to OCR only that section
- Drag & drop or file picker image input
- Runs 100% offline after model setup
- Clean MVVM architecture
- Resizable split-panel UI (image | result)
- Copy recognized text to clipboard
- Supports PNG, JPG, BMP, TIFF, GIF

---

## OCR Engines

### TrOCR
Microsoft's Transformer-based OCR model. Best for **modern English handwriting** (letters, notes, forms).
Uses a Vision Transformer (ViT) encoder and a GPT-2 autoregressive decoder, running locally via ONNX Runtime.

### Kraken HTR
An open-source HTR (Handwritten Text Recognition) engine designed for **historical documents**.
Ideal for parish registers, legal records, and manuscripts in Latin, Italian, French, Spanish, English, German, and Occitan from the 11th–21st century.
Runs through a local Flask server (`localhost:5001`) started automatically by the app.

Available Kraken models:

| Model | Best For |
|---|---|
| **McCATMuS** ★ | General historical handwriting — IT, LA, FR, ES, EN, DE, OC (16th–21st c.) |
| **TRIDIS v2** | Parish registers and legal documents (11th–16th c.) |
| **CATMuS Medieval** | Medieval French and Italian manuscripts (12th–15th c.) |
| **LECTAUREP** | Modern French administrative documents (19th–20th c.) |

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI Framework | WPF (.NET 8) |
| Architecture | MVVM (CommunityToolkit.Mvvm) |
| OCR Engine 1 | Microsoft TrOCR (ONNX Runtime) |
| OCR Engine 2 | Kraken HTR (Python Flask server) |
| Behaviors | Microsoft.Xaml.Behaviors.Wpf |

---

## Getting Started

### Requirements
- Windows 10/11
- .NET 8 Runtime
- ~2 GB free disk space for TrOCR model files
- ~200 MB for Kraken model files
- ~500 MB for Kraken Python venv (Kraken HTR only)

---

### TrOCR Setup

Download the ONNX model package from GitHub Releases and place the files here:

```
bin\net8.0-windows\models\TrOcr\
├── encoder_model.onnx
├── decoder_model.onnx
├── vocab.json
├── tokenizer.json
├── tokenizer_config.json
├── special_tokens_map.json
├── merges.txt
└── generation_config.json
```

---

### Kraken HTR Setup

Kraken HTR requires two things placed manually in the bin folder: a Python venv and the model files.

#### Step 1 — Place the Python venv

Download the Kraken Python venv from GitHub Releases and place it here:

```
bin\net8.0-windows\KrakenVenv\
└── Scripts\
    └── python.exe   ← must exist at this exact path
```

The venv must have these packages installed: `kraken`, `flask`, `flask-cors`, `pillow`

To create it yourself from scratch:

```cmd
python -m venv KrakenVenv
KrakenVenv\Scripts\pip install kraken flask flask-cors pillow
```

> Requires Python 3.10 or 3.11. Kraken is not compatible with Python 3.12+.

#### Step 2 — Place the Kraken model files

Download the `.mlmodel` files from GitHub Releases and place them here:

```
bin\net8.0-windows\models\KrakenOcr\
├── mccatmus_v1.mlmodel
├── tridis_v2_medieval_earlymodern.mlmodel
├── catmus-print-fondue-large.mlmodel
└── lectaurep_base.mlmodel
```

#### Step 3 — Run the app

The app starts the Kraken Flask server automatically when you switch to the **Kraken HTR** engine.
A status dot in the toolbar shows the server state — orange means starting, green means ready.
First startup takes 10–30 seconds while the server initializes.

---

## Full Folder Structure (after setup)

```
bin\net8.0-windows\
├── HandWritten OCR.exe
├── KrakenServer\
│   └── kraken_server_v2.py
├── KrakenVenv\              ← place manually (Python venv)
│   └── Scripts\
│       └── python.exe
├── models\
│   ├── TrOcr\               ← place manually (TrOCR ONNX files)
│   │   ├── encoder_model.onnx
│   │   ├── decoder_model.onnx
│   │   └── vocab.json  (+ other tokenizer files)
│   └── KrakenOcr\           ← place manually (Kraken .mlmodel files)
│       ├── mccatmus_v1.mlmodel
│       └── ...
```

---

## TrOCR — Model Export (Manual)

If you prefer to export the TrOCR model yourself:

> Requires Python 3.11

```cmd
python -m venv D:\trocr_env

D:\trocr_env\Scripts\pip install "optimum[onnxruntime]==1.18.0" "transformers==4.39.3"

D:\trocr_env\Scripts\pip install "torch==2.2.2" --index-url https://download.pytorch.org/whl/cpu

D:\trocr_env\Scripts\pip install onnxscript

D:\trocr_env\Scripts\optimum-cli export onnx ^
  --model microsoft/trocr-base-handwritten ^
  --task image-to-text ^
  "./models"
```

> Note: "Numpy is not available" warning at the end is safe to ignore.
