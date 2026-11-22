"""Builds src/Tubiqo.App/Assets/Tubiqo.ico from the SVG sources.

Needs Python with Pillow and Microsoft Edge (used headless to render the SVGs).
Each SVG is rendered at 1024 px and scaled down, so every size is cleanly anti-aliased.
Tubiqo-small.svg is used for 16 and 24 px, Tubiqo.svg for the rest.

    python tools/make-icon.py
"""
import pathlib, subprocess, tempfile
from PIL import Image

ASSETS = pathlib.Path(__file__).resolve().parent.parent / "src" / "Tubiqo.App" / "Assets"
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
SIZES = [16, 24, 32, 48, 64, 128, 256]
SMALL = {16, 24}
RENDER = 1024


def render(svg: pathlib.Path, tmp: pathlib.Path) -> Image.Image:
    html = tmp / (svg.stem + ".html")
    png = tmp / (svg.stem + ".png")
    html.write_text(f'<html><body style="margin:0;background:transparent">'
                    f'<img src="{svg.as_uri()}" style="width:{RENDER}px;height:{RENDER}px;display:block"></body></html>')
    subprocess.run([EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
                    "--allow-file-access-from-files", "--default-background-color=00000000",
                    f"--window-size={RENDER},{RENDER}", f"--screenshot={png}", html.as_uri()],
                   check=True, capture_output=True)
    return Image.open(png).convert("RGBA")


def main() -> None:
    with tempfile.TemporaryDirectory() as t:
        tmp = pathlib.Path(t)
        big = render(ASSETS / "Tubiqo.svg", tmp)
        small = render(ASSETS / "Tubiqo-small.svg", tmp)
    images = [(small if s in SMALL else big).resize((s, s), Image.LANCZOS) for s in SIZES]
    out = ASSETS / "Tubiqo.ico"
    images[-1].save(out, format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[:-1])
    print(f"Wrote {out}")


if __name__ == "__main__":
    main()
