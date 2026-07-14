# Voice Translator

Voice Translator — локальное приложение для Windows 11, которое распознаёт
русскую речь, переводит её на один из 16 языков и озвучивает перевод выбранным
голосом. Распознавание, перевод и синтез выполняются на компьютере без отправки
речи во внешние API.

Обычные записи сессии, расшифровки и переводы не сохраняются. Именованные
голосовые профили являются единственным исключением: образец голоса шифруется
Windows DPAPI для текущего пользователя и хранится в
`%LOCALAPPDATA%\VoiceTranslator\VoiceProfiles`.

### Требования

- Windows 11 x64;
- видеокарта NVIDIA с CUDA, целевая конфигурация — RTX 3070 8 ГБ;
- .NET 10 SDK или локальная версия из папки `.dotnet`;
- Python 3.11 и [uv](https://docs.astral.sh/uv/);
- для вывода перевода в Discord, Telegram и другие приложения — установленный
  виртуальный аудиокабель, например VB-CABLE или VoiceMeeter.

NLLB и XTTS предназначены для личного некоммерческого использования и имеют
собственные лицензионные ограничения. Перед загрузкой моделей ознакомьтесь с
их лицензиями.

### Установка и запуск

В PowerShell из корня репозитория создайте Python-окружение и установите
зависимости:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\worker\bootstrap.ps1
```

Затем загрузите и проверьте модели:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\worker\bootstrap.ps1 -DownloadModels -AcceptNoncommercial
```

После установки запускайте приложение двойным щелчком по файлу:

```text
START Voice Translator.cmd
```

### Как пользоваться

1. Выберите язык перевода, микрофон и режим вывода.
2. Создайте голосовой профиль: нажмите **Новый**, укажите имя, начните запись и
   произнесите 3–15 секунд обычной речи. Нажмите **Завершить** или дождитесь
   автоматической остановки через 15 секунд.
3. Выберите сохранённый профиль. Он автоматически применяется ко всем языкам и
   новым сессиям.
4. Выберите режим производительности:
   - **Экономный** — минимальная нагрузка и меньшая модель распознавания;
   - **Баланс** — средняя модель и умеренное использование памяти;
   - **Производительность** — максимальная скорость, FP16 на GPU и повышенное
     использование видеопамяти.
5. Нажмите **Запустить** и говорите фразами. Индикатор показывает текущий этап
   обработки и приблизительный прогресс.

Для Discord или Telegram выберите режим **Виртуальный микрофон**, а затем
укажите обнаруженный виртуальный выход. В самом Discord или Telegram выберите
парный вход этого аудиокабеля в качестве микрофона. Приложение не устанавливает
драйвер виртуального кабеля самостоятельно.

При закрытии Voice Translator синхронно останавливает аудиосессию и завершает
всё дерево процессов Python. Worker также привязан к Windows Job Object, поэтому
его дочерние процессы закрываются даже при аварийном завершении .NET-приложения.

---

Windows 11 desktop application for local phrase-by-phrase translation of
Russian speech into 16 target languages with reusable local voice profiles.

Transcripts, translations, and ordinary session audio are kept in memory and
are not intentionally persisted. Named voice-reference profiles are the only
exception: they are encrypted for the current Windows user with DPAPI and
stored under `%LOCALAPPDATA%\VoiceTranslator\VoiceProfiles`.

## License restriction

This configuration is intended only for personal, noncommercial use. NLLB and
XTTS have model-specific license restrictions. Review their licenses before
downloading them and pass `--accept-noncommercial` only if you accept those
terms.

## Prerequisites

- Windows 11 x64
- NVIDIA GPU with CUDA support; the tested target is an RTX 3070 8 GB
- .NET 10 SDK, or the workspace-local SDK in `.dotnet`
- Python 3.11 and [uv](https://docs.astral.sh/uv/)
- Optional signed virtual audio cable for virtual-microphone routing

In PowerShell:

```powershell
$dotnet = "$PWD\.dotnet\dotnet.exe"
$uv = "$env:USERPROFILE\.local\bin\uv.exe"
```

## Install the worker

Create the local Python 3.11 environment and install CUDA/ML dependencies:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\worker\bootstrap.ps1
```

For development tests, also install the test group:

```powershell
& $uv sync --project worker --extra test --extra ml
```

## Download verified models

Models are pinned to exact Hugging Face commit revisions. The downloader
creates SHA-256 install receipts and converts NLLB to CTranslate2 format.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\worker\bootstrap.ps1 -DownloadModels -AcceptNoncommercial
```

By default, verified artifacts are installed under:

```text
%USERPROFILE%\.voice-translator\models
```

Future launches verify the install receipts before loading any model.

## Build and test

```powershell
& $dotnet restore VoiceTranslator.slnx
& $dotnet format VoiceTranslator.slnx --verify-no-changes --no-restore
& $dotnet build VoiceTranslator.slnx --configuration Release --no-restore
& $dotnet test VoiceTranslator.slnx --configuration Release --no-build --no-restore
& worker\.venv\Scripts\python.exe -m pytest worker\tests -q
```

## Run

For normal use, double-click:

```text
START Voice Translator.cmd
```

The launcher uses the workspace-local .NET SDK, verifies that the worker
environment and model inventory exist, then starts the WPF application. It
writes startup output to `artifacts\logs\voice-translator-launch.log`.

The WPF application starts and stops its own worker. Select an existing voice
profile to translate the first completed phrase immediately. To create a new
profile, click **Новый**, enter a name, click **Начать запись**, speak normally
for 3–15 seconds, and click **Завершить**. Recording stops automatically after
15 seconds. The same encrypted profile can be selected for any target language,
renamed, or deleted in the application.

The Python worker and all of its child processes run in a Windows Job Object.
Closing the WPF application stops the host synchronously and kills the complete
worker process tree; Windows also closes the tree if the .NET host terminates
unexpectedly.

Choose **Экономный** for the smallest ASR model and lowest VRAM use,
**Баланс** for the medium ASR model with conservative memory residency, or
**Производительность** for FP16 GPU inference, beam size 1, and a resident NLLB
translator. Performance mode uses the most GPU memory and automatically falls
back through Balance to Economical after CUDA OOM. The worker binds to `127.0.0.1`,
receives a new 256-bit token for every launch, and rejects unauthenticated
requests.

Virtual output routes translated audio to an installed Windows virtual audio
cable such as VB-CABLE, VoiceMeeter, Wave Link, or SteelSeries Sonar. Select the
cable's paired capture endpoint as the microphone in Discord, Telegram, or
another voice application. Voice Translator does not install a virtual-audio
driver itself.

`VoiceTranslator.WorkerHost` remains available as a command-line diagnostic
host, but it must not be started at the same time as the WPF application.

Do not add downloaded models, captured audio, transcripts, translations,
decrypted speaker references, embeddings, or launch tokens to repository files
or logs.
