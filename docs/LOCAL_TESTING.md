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
