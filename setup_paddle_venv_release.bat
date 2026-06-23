@echo off
REM ============================================================
REM  HandWritten OCR — first-time setup
REM  Run this ONCE before launching HandWritten OCR.exe
REM  Requires Python 3.8-3.12 installed and on PATH.
REM ============================================================

set VENV_DIR=%~dp0PaddleVenv

echo.
echo ============================================================
echo  Setting up PaddleOCR environment...
echo  This will download ~800 MB. Please keep this window open.
echo  Location: %VENV_DIR%
echo ============================================================
echo.

python --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Python not found.
    echo Please install Python 3.11 from https://python.org/downloads
    echo Make sure to check "Add Python to PATH" during install.
    pause
    exit /b 1
)
echo Python found:
python --version

echo.
echo [1/5] Creating virtual environment...
python -m venv "%VENV_DIR%"
if errorlevel 1 (
    echo ERROR: Failed to create venv. Make sure Python 3.8-3.12 is installed.
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
    echo ERROR: PaddlePaddle install failed. Check your internet connection.
    pause
    exit /b 1
)
echo Done.

echo.
echo [4/5] Installing PaddleOCR + PaddleX (OCR extras)...
"%VENV_DIR%\Scripts\python.exe" -m pip install paddleocr
if errorlevel 1 (
    echo ERROR: PaddleOCR install failed. Check your internet connection.
    pause
    exit /b 1
)
echo Done.

echo.
echo [5/5] Installing Flask server dependencies...
"%VENV_DIR%\Scripts\python.exe" -m pip install flask flask-cors pillow
if errorlevel 1 (
    echo ERROR: Flask/Pillow install failed.
    pause
    exit /b 1
)
echo Done.

echo.
echo ============================================================
echo  Setup complete!
echo.
echo  You can now launch: HandWritten OCR.exe
echo.
echo  NOTE: On first PaddleOCR use, ~100 MB of OCR models will
echo  download automatically. Internet required for first run.
echo ============================================================
pause
