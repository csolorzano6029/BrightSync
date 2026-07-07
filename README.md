# BrightSync

Utilidad de bandeja para Windows que controla **brillo, temperatura de color y filtros
de accesibilidad**, y —a diferencia de Gammy— **aplica la última configuración guardada
cada vez que arranca con Windows**.

## Características

- **Brillo híbrido**
  - Hardware real: **DDC/CI** (monitores externos) y **WMI** (paneles de portátil). Llega a 0–100 % de backlight real.
  - Fallback por **gamma** (software) cuando no hay control de hardware.
- **Temperatura de color** (luz azul) por gamma, 3000 K (cálido) – 6500 K (neutro).
- **Filtros de color / accesibilidad** vía Magnification API:
  escala de grises, invertir, alto contraste y **corrección de daltonismo** (protanopia,
  deuteranopia, tritanopia).
- **Aplica la config al arrancar** (el fix principal) y **reaplica** si Windows resetea la
  gamma (pantalla completa, reanudar de suspensión, RDP, cambio de resolución).
- **Perfiles** múltiples (día/noche/juego…) con cambio rápido.
- **Atajos de teclado globales** (por defecto Ctrl+Alt+RePág/AvPág, Ctrl+Alt+P, Ctrl+Alt+F).
- **Auto-atenuación por contenido** (analiza la pantalla y ajusta el brillo).
- **Autoarranque** por clave Run (sin admin) o **arranque temprano** por Tarea Programada.
- Instancia única, icono en bandeja, config en JSON.

## Requisitos

- Windows 10/11
- **.NET 8 Desktop Runtime** (ya lo tienes si instalaste el SDK)

## Compilar y ejecutar

```powershell
# Ejecutar en desarrollo
dotnet run --project BrightSync

# Generar el ejecutable distribuible (un solo .exe)
dotnet publish BrightSync -c Release
# Resultado: bin/Release/net8.0-windows/win-x64/publish/BrightSync.exe
```

## Uso

- Doble clic en el icono de la bandeja → **Ajustes**.
- Clic derecho → brillo rápido, perfiles, filtros, autoarranque.
- La configuración se guarda en `%AppData%\BrightSync\config.json`.
- Log de diagnóstico en `%AppData%\BrightSync\log.txt`.

## Notas técnicas

- **Todo en una matriz de color**: brillo profundo + temperatura + filtro se componen en una
  única matriz 5x5 (Magnification API), en el orden correcto (color → filtro → temperatura →
  atenuación). Así invertir/contraste funcionan aunque la pantalla esté muy oscura, la
  temperatura cálida no choca con el límite de gamma, y la atenuación llega casi a negro sin
  el tope ~50 % de `SetDeviceGammaRamp`. El **hardware (DDC/CI/WMI)** baja además el backlight real.
- **Fallback**: si Magnification no está disponible, se usa gamma (con degradación ante el
  clamp de Windows) + un veil negro (`OverlayDimmer`) para la atenuación profunda; en ese modo
  los filtros de color no están disponibles.
- **Límite de gamma de Windows** (solo afecta al fallback): por defecto Windows no deja atenuar
  por gamma por debajo de ~50 %. La opción «Desbloquear gamma profundo» escribe la clave de
  registro `GdiIcmGammaRange=256` (admin + reiniciar sesión) para levantar el límite.

## Estructura

```
Program.cs                 Entrada, instancia única
TrayApplicationContext.cs  Icono de bandeja, orquestación, eventos del sistema
Core/
  Config.cs                Modelo + persistencia JSON
  DisplayEngine.cs         Orquesta perfil = brillo + temperatura + filtro
  HardwareBrightness.cs    WMI + DDC/CI
  GammaController.cs        Brillo/temperatura por gamma (con degradación)
  ColorEffectController.cs  Filtros de color (Magnification API)
  AutoDimEngine.cs         Atenuación por contenido
  HotkeyManager.cs         Atajos globales
  StartupManager.cs        Autoarranque (Run key / Tarea Programada)
  SystemTweaks.cs          Desbloqueo de gamma (registro, elevado)
  Log.cs                   Log de diagnóstico
UI/
  SettingsForm.cs          Ventana de ajustes
```
