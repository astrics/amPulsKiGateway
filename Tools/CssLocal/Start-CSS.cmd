@echo off
setlocal
set "CSS_ROOT=D:\AI\Data\CSS"
py -3.12 -u -X utf8 "%~dp0css_local.py" --root "%CSS_ROOT%" process-inbox --seed "%CSS_ROOT%\october-results.json" --rules "%CSS_ROOT%\manual-rules-20261106.json" --prompt-key
set "CSS_EXIT=%ERRORLEVEL%"
if not "%CSS_EXIT%"=="0" echo Verarbeitung unterbrochen. Exitcode: %CSS_EXIT%
pause
exit /b %CSS_EXIT%
