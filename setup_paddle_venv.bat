@echo off
REM ============================================================
REM  Setup PaddleOCR virtual environment for HandWritten OCR
REM  Run this ONCE from the project root before using PaddleOCR.
REM  Requires Python 3.9-3.12 installed and on PATH.
REM ============================================================

set VENV_DIR=%~dp0HandWritten OCR\bin\Debug\net8.0-windows\PaddleVenv

echo Creating Python venv at: %VENV_DIR%
python -m venv "%VENV_DIR%"
if errorlevel 1 (
    echo ERROR: Failed to create venv. Make sure Python 3.9-3.12 is installed.
    pause
    exit /b 1
)

echo Upgrading pip...
"%VENV_DIR%\Scripts\python.exe" -m pip install --upgrade pip

echo Installing PaddlePaddle (CPU-only, lightweight for low-spec PCs)...
"%VENV_DIR%\Scripts\pip.exe" install paddlepaddle

echo Installing PaddleOCR...
"%VENV_DIR%\Scripts\pip.exe" install paddleocr

echo Installing Flask server dependencies...
"%VENV_DIR%\Scripts\pip.exe" install flask flask-cors pillow

echo.
echo ============================================================
echo  PaddleOCR venv setup complete!
echo  Models (~15 MB) will auto-download on first OCR run.
echo ============================================================
pause
