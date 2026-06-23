@echo off
REM ============================================================
REM  setup.bat  —  PP-OCRv6 one-time setup
REM  Run this ONCE from the project root (two levels up).
REM  Requires Python 3.9-3.12 installed and on PATH.
REM ============================================================

REM Resolve the project root (two directories up from models\PaddleOcr\)
pushd "%~dp0..\.."
set PROJECT_ROOT=%CD%
popd

set VENV_DIR=%PROJECT_ROOT%\HandWritten OCR\bin\Debug\net8.0-windows\PaddleVenv

echo.
echo ============================================================
echo  Setting up PP-OCRv6 environment...
echo  Location: %VENV_DIR%
echo ============================================================
echo.

python --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Python not found. Install Python 3.9-3.12 and add it to PATH.
    pause
    exit /b 1
)
echo Python found:
python --version

echo.
echo [1/5] Creating virtual environment...
python -m venv "%VENV_DIR%"
if errorlevel 1 (
    echo ERROR: Failed to create venv.
    pause
    exit /b 1
)
echo Done.

echo.
echo [2/5] Upgrading pip...
"%VENV_DIR%\Scripts\python.exe" -m pip install --upgrade pip
if errorlevel 1 (
    echo WARNING: pip upgrade failed, continuing...
)

echo.
echo [3/5] Installing PaddlePaddle (CPU)...
"%VENV_DIR%\Scripts\python.exe" -m pip install paddlepaddle
if errorlevel 1 (
    echo ERROR: PaddlePaddle install failed.
    pause
    exit /b 1
)
echo Done.

echo.
echo [4/5] Installing PaddleOCR + PaddleX (OCR extras)...
REM paddleocr depends on paddlex[ocr], so both are installed together
"%VENV_DIR%\Scripts\python.exe" -m pip install paddleocr
if errorlevel 1 (
    echo ERROR: PaddleOCR install failed.
    pause
    exit /b 1
)
echo Done.

echo.
echo [5/5] Installing Flask and Pillow...
"%VENV_DIR%\Scripts\python.exe" -m pip install flask flask-cors pillow
if errorlevel 1 (
    echo ERROR: Flask/Pillow install failed.
    pause
    exit /b 1
)
echo Done.

echo.
echo ============================================================
echo  Setup complete! Installed packages:
"%VENV_DIR%\Scripts\python.exe" -m pip list | findstr /i "paddle flask pillow"
echo.
echo  Venv: %VENV_DIR%
echo.
echo  PP-OCRv6 models will auto-download on first OCR run.
echo ============================================================
pause
