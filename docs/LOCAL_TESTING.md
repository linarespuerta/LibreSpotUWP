# Pruebas locales y límite del emulador

## Qué está listo

El repositorio ya contiene una `librespot.dll` compilada para `thumbv7a-uwp-windows-msvc` (ARM32/UWP), que es la arquitectura del Lumia 950 XL. Su ABI y sus exportaciones se validaron contra `Interop/LibreSpot.cs`, incluida la exportación de Spotify DJ.

La DLL ARM no puede cargarse en un proceso x64 de este PC. Por tanto, copiarla a una aplicación de escritorio no es una prueba válida; produciría un error de arquitectura antes de ejecutar el código.

## Qué sí podemos probar aquí

- Compilación reproducible del núcleo Rust y de la DLL ARM.
- ABI C# ↔ Rust: nombres de exportación, tamaños de estructuras, tipos y convenciones de llamada.
- Inspección PE: máquina ARM, subsistema UWP/AppContainer, imports y ausencia de exportaciones faltantes.
- Compilación estática del host UWP y validación de manifest, capacidades, referencias y contenido del paquete.
- Pruebas unitarias, de serialización, configuración, estados de reproducción y contratos de Home Assistant que no necesiten el dispositivo.
- Un binario separado de librespot para Windows x64, si se necesita probar el protocolo Spotify Connect y mDNS desde el PC. Ese binario sirve para probar red/protocolo, no audio ni compatibilidad ARM del Lumia.

## Qué no demuestra el PC

- Que Windows 10 Mobile cargue la DLL y el paquete ARM.
- Que el backend de audio reproduzca por el DAC/salida de 3,5 mm del Lumia.
- Que el anuncio `_spotify-connect._tcp` atraviese correctamente el sandbox UWP en la red doméstica.
- Transferencia desde una segunda cuenta Premium, reconexión Wi-Fi, consumo térmico/batería o comportamiento de suspensión.

Esas comprobaciones siguen siendo una puerta de hardware y deben ejecutarse en el Lumia, con los resultados registrados en `TEST_PLAN.md`. No se deben marcar como aprobadas por una prueba de escritorio.

## Qué es el PFX de desarrollo

`LibreSpotUWP_TemporaryKey.pfx` es un contenedor PKCS#12 que incluye un certificado y su clave privada. Visual Studio/MSBuild lo usa para firmar el APPX; Windows no instala un paquete UWP firmado con una identidad que no pueda verificar.

Es una credencial de desarrollo local, no una clave de Spotify ni una dependencia de librespot. No debe subirse al repositorio (`.gitignore` ya excluye `*.pfx`) ni compartirse. Se puede generar desde Visual Studio al crear/seleccionar el certificado de firma del proyecto, o usar uno de desarrollo existente bajo control del usuario. Para instalar el APPX en el Lumia también hay que confiar/instalar el certificado público en el dispositivo según el modo de desarrollo habilitado.

La ausencia del PFX solo bloquea el empaquetado/instalación firmado. No bloquea la compilación del núcleo Rust, la validación de la DLL ni las pruebas locales descritas arriba.

## Relación con los objetivos de `prompt.md`

Esto mantiene el plan original: primero reproducibilidad y ABI (Phase 2), después discovery y reproducción verificadas en el Lumia (Phase 3), y solo luego UI/ Home Assistant/fiabilidad. No se sustituye la validación física por una afirmación de emulación; se adelanta todo lo que el PC puede demostrar sin arriesgar la instalación funcional del teléfono.

## Pruebas de Home Assistant (sin dispositivo)

Todo lo siguiente se ejecuta en Fedora y no toca el Home Assistant del homelab. Requiere `uv`, `dotnet` 8 y `podman-compose`.

| Qué | Comando (desde `LibreSpotUWP/`) |
|---|---|
| Núcleo del contrato en C# (mapper, parser, throttle, junto con el resto del harness) | `dotnet run --project tests/LibreSpotUWP.Playback.Tests` |
| Comprobación de tipos WinRT del enlace (C# 7.3 contra los metadatos del SDK 10.0.19041; no compila XAML ni .NET Native) | `dotnet build tests/WinRtTypeCheck` |
| Integración `lumia_hifi`: contrato, config flow, endpoint, entidad, secretos | `cd integrations/home_assistant && uv run pytest` |
| Lint y tipos de la integración | `cd integrations/home_assistant && uv run ruff check . && uv run mypy` |
| Prueba de humo: Home Assistant desechable en podman + simulador del Lumia | `integrations/home_assistant/dev/smoke.sh` |
| Simulador a mano contra un Home Assistant de desarrollo | `LUMIA_SIM_SECRET=<secreto> uv run python tools/lumia_sim.py --url ws://localhost:8123/api/lumia_hifi/v1/link` |

Los fixtures de `contracts/lumia_hifi/v1/fixtures/` son compartidos: Python los valida contra los JSON Schema y contra el validador de la integración, y C# los usa como salida de referencia del mapper y como entrada del parser. Si cambias el contrato, cambia primero el esquema y los fixtures; los tres lados deben seguir en verde.

`smoke.sh` descarga la imagen `ghcr.io/home-assistant/home-assistant` (fijada a una versión), la publica solo en `127.0.0.1:18123` y borra el contenedor al terminar (`KEEP=1` lo conserva). Para el Lumia real queda pendiente WP7 (`HOME_ASSISTANT_CODE_ROUND.md`); estas pruebas no sustituyen la validación en el teléfono.
