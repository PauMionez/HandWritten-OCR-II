# HandWritten OCR

A WPF desktop application that recognizes handwritten text from images
using Microsoft TrOCR running locally via ONNX Runtime.
No internet connection, no API calls, no cloud — runs 100% on your machine.

---

## Preview

> Drop a handwritten image → Click Run OCR → Get recognized text instantly

---

## Features

- Handwritten text recognition powered by Microsoft TrOCR
- Drag & drop or file picker image input
- Runs 100% offline after model setup
- Clean MVVM architecture — zero code-behind
- Resizable split-panel UI (image | result)
- Copy recognized text to clipboard
- Supports PNG, JPG, BMP, TIFF, GIF

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI Framework | WPF (.NET 8) |
| Architecture | MVVM (no code-behind) |
| AI Model | Microsoft TrOCR (Transformer-based OCR) |
| Inference Engine | Microsoft.ML.OnnxRuntime |
| MVVM Toolkit | CommunityToolkit.Mvvm |
| Behaviors | Microsoft.Xaml.Behaviors.Wpf |
| Model Format | ONNX |

---

## How It Works

User drops image
↓

Image preprocessed → resized 384x384, normalized

↓

ONNX Encoder (Vision Transformer) reads image → hidden states

↓

ONNX Decoder loops → generates tokens one by one (autoregressive)

↓

GPT-2 BPE vocab.json decodes tokens → readable text

↓

Result displayed in UI


---

## Getting Started

### Requirements
- Windows 10/11
- .NET 8 Runtime
- ~2GB free disk space for model files

### 1. Clone the repo

### 2. Download the model files
Download the ONNX model package from
GitHub Releases

Extract and place the files here:

HandWritten OCR/bin/Debug/net8.0-windows/models/
├── encoder_model.onnx
├── decoder_model.onnx
└── vocab.json

### 3. Build and Run
Open the solution in Visual Studio 2022 and press F5

---

## Model Setup (Manual Export)
If you prefer to export the model yourself using Python:

>Requires Python 3.11

### CMD
python -m venv D:\trocr_env

D:\trocr_env\Scripts\pip.exe install "optimum[onnxruntime]==1.18.0" "transformers==4.39.3"

D:\trocr_env\Scripts\pip.exe install "torch==2.2.2" --index-url https://download.pytorch.org/whl/cpu

D:\trocr_env\Scripts\pip.exe install onnxscript

D:\trocr_env\Scripts\optimum-cli export onnx ^
  --model microsoft/trocr-base-handwritten ^
  --task image-to-text ^
  "./models"

> Note: "Numpy is not available" warning at the end is safe to ignore. The model files are saved regardless.
