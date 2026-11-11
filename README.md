# Wallpaper Aura

Wallpaper Aura maps the colours and spatial gradient of the current Windows wallpaper to addressable LEDs through OpenRGB. It supports mirrored patterns, smooth wallpaper changes and eight-point fan rings. This is an independent community project, not an ASUS, Phanteks, or OpenRGB product.

The included **EX5 Pro example is a wiring profile, not universal hardware detection**. It was observed on a Phanteks EX5 Pro connected to an ASUS ROG STRIX Z790-A GAMING WIFI D4, with three fans on the first ARGB header and a separately connected GPU on the third. Physical LEDs can share an address, and the actual fan wiring and LED counts may be different. The app cannot infer your wiring or the number of physical LEDs.

## Requirements

- Windows 10/11 and Python 3.10 or newer (64-bit recommended).
- An [OpenRGB](https://openrgb.org/) installation whose SDK server can access your motherboard, and the motherboard's ARGB headers correctly connected to your case. OpenRGB must recognize the device in Direct mode.
- A Windows picture or slideshow wallpaper. The first monitor's wallpaper is used. Video/live wallpapers and capture of the screen are not supported.
- Other RGB software may compete for the controller. Stop competing lighting software manually if needed; Wallpaper Aura does not change Windows services or start OpenRGB for you. OpenRGB itself may require elevated rights on some motherboards.

## Install

In PowerShell, with Git installed:

```powershell
py -m pip install "git+https://github.com/Evangelion91/wallpaper-aura.git"
```

Alternatively download the source archive, unpack it and run `py -m pip install .` from its directory. Python dependencies are installed from PyPI. No OpenRGB executable or proprietary software is bundled.

## Configure and run

1. Start OpenRGB, make sure it detects the motherboard, and enable its SDK server on the local machine (default port 6742). Avoid binding it to an untrusted network.
2. Inspect the detected devices and zones without changing LEDs:

   ```powershell
   wallpaper-aura list
   ```

3. Create an example profile, then open it in a text editor:

   ```powershell
   wallpaper-aura init --output .\profile.json
   ```

   Set `device_name` to the **exact** name shown by `list`, and adjust `board_led_count`, `header_led_counts`, and fan/GPU segments to match your OpenRGB zones and actual wiring. Headers are zero-based in the JSON; fan segments each contain eight addressable positions. Set `gpu_header` to `null` and `gpu_segments` to `[]` when no GPU is connected to a motherboard ARGB header. The board zone must be zone 0, followed by the configured headers in order.
4. The example starts with `allow_resize: false`. If the LED counts shown by `list` differ from your verified wiring, OpenRGB may need resizeable zones: review the requested counts, then set `allow_resize` to `true` to permit changes. The program checks the board and zone layout before writing colours and fails if it cannot match them.
5. Start the app:

   ```powershell
   wallpaper-aura run --config C:\path\to\your-profile.json
   ```

   Stop it with Ctrl+C. OpenRGB and its SDK server must remain running. The program does not restore a previous lighting effect when stopped; choose one in your lighting software. It does not configure autostart automatically. If desired, create a Windows scheduled task for this command after testing it interactively and ensure OpenRGB starts first.

To inspect calculated colours without writing to hardware:

```powershell
wallpaper-aura preview C:\path\to\image.png --config C:\path\to\your-profile.json
```

`mirror` flips the sampling horizontally. Board and GPU brightness values are normalized to 0–1; case brightness is intentionally lower to retain colour definition. The program samples a wallpaper when its file changes and smoothly transitions its LEDs. It does not reproduce motion from video wallpapers. Existing physical connections can duplicate addresses between fans and strips; that is not a software-detectable LED count.

## Privacy and scope

The image is processed locally and is not uploaded. The `run` command logs wallpaper paths to its console. This repository does not contain users' wallpapers. The separate LCD metrics/background controller is not part of this package. No warranty is provided; check OpenRGB compatibility and your ARGB wiring before configuring a controller.

## Development

```powershell
py -m pip install -e .
py -m unittest discover -s tests -v
```

Licensed under MIT. Dependencies have their own licenses.
