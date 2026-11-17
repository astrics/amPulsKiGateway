@echo off
setlocal
set "CSS_ROOT=D:\AI\Data\CSS"
set "PY_ARGS="
if defined CSS_PYTHON goto check_python
set "CSS_PYTHON=%CSS_ROOT%\.venv\Scripts\python.exe"
if exist "%CSS_PYTHON%" goto check_python
py -3.12 -c "import sys" >nul 2>&1
if not errorlevel 1 (
    set "CSS_PYTHON=py"
    set "PY_ARGS=-3.12"
    goto check_python
)
set "CSS_PYTHON=%LocalAppData%\Programs\Python\Python312\python.exe"
if exist "%CSS_PYTHON%" goto check_python
set "CSS_PYTHON=%ProgramFiles%\Python312\python.exe"
if exist "%CSS_PYTHON%" goto check_python
set "CSS_PYTHON=python"

:check_python
"%CSS_PYTHON%" %PY_ARGS% -c "import sys; sys.exit(sys.version_info[:2] != (3, 12))" >nul 2>&1
if errorlevel 1 goto no_python
"%CSS_PYTHON%" %PY_ARGS% -c "import pandas, pyreadstat" >nul 2>&1
if errorlevel 1 goto missing_packages
if /i "%~1"=="--check" (
    echo Python und CSS-Pakete sind einsatzbereit.
    exit /b 0
)
set "CSS_RULES=%CSS_ROOT%\manual-rules.json"
if not exist "%CSS_RULES%" set "CSS_RULES=%CSS_ROOT%\manual-rules-20261106.json"
"%CSS_PYTHON%" %PY_ARGS% -u -X utf8 "%~dp0css_local.py" --root "%CSS_ROOT%" process-inbox --seed "%CSS_ROOT%\october-results.json" --rules "%CSS_RULES%" --prompt-key
set "CSS_EXIT=%ERRORLEVEL%"
if not "%CSS_EXIT%"=="0" echo Verarbeitung unterbrochen. Exitcode: %CSS_EXIT%
pause
exit /b %CSS_EXIT%

:no_python
echo Python 3.12 wurde nicht gefunden. Installiere Python 3.12 oder setze CSS_PYTHON auf den Pfad zu python.exe.
pause
exit /b 1

:missing_packages
echo Fuer diese Python-Installation fehlen pandas oder pyreadstat.
echo Einmalig in cmd ausfuehren:
echo "%CSS_PYTHON%" %PY_ARGS% -m pip install pandas pyreadstat==1.3.8
pause
exit /b 1
