#!/usr/bin/env python3
"""Generate the OmniKey Vault application icon (.ico).

Creates a professional-looking icon featuring a shield with a keyhole,
representing security and vault concepts. The icon is generated in multiple
sizes (16, 32, 48, 64, 128, 256) and saved as a Windows .ico file.
"""

from PIL import Image, ImageDraw, ImageFont
import math
import os

def create_gradient(width, height, color1, color2):
    """Create a vertical gradient image."""
    img = Image.new('RGBA', (width, height), (0, 0, 0, 0))
    pixels = img.load()
    for y in range(height):
        ratio = y / max(height - 1, 1)
        r = int(color1[0] * (1 - ratio) + color2[0] * ratio)
        g = int(color1[1] * (1 - ratio) + color2[1] * ratio)
        b = int(color1[2] * (1 - ratio) + color2[2] * ratio)
        for x in range(width):
            pixels[x, y] = (r, g, b, 255)
    return img

def draw_shield(draw, cx, cy, w, h, fill, outline=None, outline_width=0):
    """Draw a shield shape centered at (cx, cy) with given width and height."""
    # Shield points
    top = cy - h // 2
    bottom = cy + h // 2
    left = cx - w // 2
    right = cx + w // 2
    shoulder_y = top + int(h * 0.35)
    curve_start = cy + int(h * 0.15)

    points = [
        (cx, top),                      # top center
        (right, top + int(h * 0.08)),   # top right
        (right, shoulder_y),             # right shoulder
        (right - int(w * 0.05), curve_start),  # right curve start
        (cx, bottom),                    # bottom point
        (left + int(w * 0.05), curve_start),   # left curve start
        (left, shoulder_y),              # left shoulder
        (left, top + int(h * 0.08)),    # top left
    ]

    draw.polygon(points, fill=fill, outline=outline, width=outline_width)

def draw_keyhole(draw, cx, cy, size, color):
    """Draw a keyhole shape at (cx, cy)."""
    # Circle part (top)
    circle_radius = size // 3
    circle_y = cy - size // 6
    draw.ellipse(
        [cx - circle_radius, circle_y - circle_radius,
         cx + circle_radius, circle_y + circle_radius],
        fill=color
    )
    # Trapezoid part (bottom)
    top_w = size // 4
    bot_w = size // 8
    top_y = circle_y + circle_radius - size // 12
    bot_y = cy + size // 2
    points = [
        (cx - top_w, top_y),
        (cx + top_w, top_y),
        (cx + bot_w, bot_y),
        (cx - bot_w, bot_y),
    ]
    draw.polygon(points, fill=color)

def create_icon(size):
    """Create the icon at the given size."""
    # Create transparent background
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))

    # Gradient background (rounded square)
    bg = create_gradient(size, size, (45, 55, 82), (30, 35, 55))

    # Create a mask for rounded corners
    mask = Image.new('L', (size, size), 0)
    mask_draw = ImageDraw.Draw(mask)
    radius = size // 6
    mask_draw.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)

    # Apply gradient with rounded corners
    img.paste(bg, (0, 0), mask)

    draw = ImageDraw.Draw(img)

    # Draw shield
    shield_w = int(size * 0.62)
    shield_h = int(size * 0.72)
    shield_cx = size // 2
    shield_cy = size // 2 + int(size * 0.02)

    # Shield outline (slightly larger, lighter)
    if size >= 48:
        draw_shield(draw, shield_cx, shield_cy, shield_w + 2, shield_h + 2,
                    fill=None, outline=(100, 120, 160, 200), outline_width=max(1, size // 64))

    # Shield fill - gradient-like effect using two layers
    draw_shield(draw, shield_cx, shield_cy, shield_w, shield_h,
                fill=(70, 90, 140, 255))

    # Inner shield highlight
    if size >= 32:
        draw_shield(draw, shield_cx, shield_cy - int(size * 0.03),
                    int(shield_w * 0.85), int(shield_h * 0.85),
                    fill=(85, 110, 165, 255))

    # Draw keyhole
    keyhole_size = int(size * 0.28)
    keyhole_color = (240, 245, 255, 255)
    draw_keyhole(draw, shield_cx, shield_cy, keyhole_size, keyhole_color)

    # Add subtle glow around keyhole for larger sizes
    if size >= 64:
        glow = Image.new('RGBA', (size, size), (0, 0, 0, 0))
        glow_draw = ImageDraw.Draw(glow)
        glow_radius = keyhole_size // 3
        glow_y = shield_cy - keyhole_size // 6
        for r in range(glow_radius + 4, glow_radius, -1):
            alpha = max(0, 60 - (r - glow_radius) * 20)
            glow_draw.ellipse(
                [shield_cx - r, glow_y - r, shield_cx + r, glow_y + r],
                fill=(100, 150, 255, alpha)
            )
        img = Image.alpha_composite(img, glow)
        draw = ImageDraw.Draw(img)
        # Redraw keyhole on top
        draw_keyhole(draw, shield_cx, shield_cy, keyhole_size, keyhole_color)

    return img

def main():
    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = [create_icon(s) for s in sizes]

    output_dir = os.path.join(os.path.dirname(__file__), '..', 'images')
    os.makedirs(output_dir, exist_ok=True)

    ico_path = os.path.join(output_dir, 'okv-icon.ico')
    png_path = os.path.join(output_dir, 'okv-icon.png')

    # Save as ICO
    images[-1].save(ico_path, format='ICO',
                    sizes=[(s, s) for s in sizes],
                    append_images=images[:-1])

    # Also save a large PNG for reference
    create_icon(512).save(png_path, format='PNG')

    print(f"Icon saved to: {ico_path}")
    print(f"PNG reference saved to: {png_path}")
    print(f"Sizes: {sizes}")

if __name__ == '__main__':
    main()
