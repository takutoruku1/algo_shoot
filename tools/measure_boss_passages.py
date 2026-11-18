import argparse
import json
import math
from pathlib import Path

import cv2
import numpy as np


def point(x, y, scale):
    return round((x - 66) * scale), round(y * scale)


def draw_frame(frame, shapes, scale):
    blocked = np.zeros((216 * scale + 1, (384 - 66) * scale + 1), np.uint8)
    for kind, x, y, end_x, end_y, radius in frame["hazards"]:
        a, b = point(x, y, scale), point(end_x, end_y, scale)
        r = round(radius * scale)
        if kind == 2:
            cv2.rectangle(blocked, a, b, 1, -1)
        elif kind == 3:
            outside = np.ones_like(blocked)
            cv2.circle(outside, a, r, 0, -1)
            blocked |= outside
        else:
            cv2.circle(blocked, a, r, 1, -1)
            if a != b:
                cv2.circle(blocked, b, r, 1, -1)
                cv2.line(blocked, a, b, 1, max(1, 2 * r))
    for x, y, end_x, end_y in frame["barriers"]:
        cv2.rectangle(blocked, point(x, y, scale), point(end_x, end_y, scale), 1, -1)
    for model, x, y, xx, xy, yx, yy, radius, *_ in frame["shots"]:
        if not shapes.get(model):
            radius *= max(math.hypot(xx, xy), math.hypot(yx, yy))
            cv2.circle(blocked, point(x, y, scale), round(radius * scale), 1, -1)
            continue
        transform = np.array([[xx, xy], [yx, yy]])
        shift = np.array([x - 66, y]) * scale
        for polygon in shapes[model]:
            vertices = np.rint(polygon @ transform * scale + shift).astype(np.int32)
            cv2.fillConvexPoly(blocked, vertices, 1)
    return blocked


def measure(path, target, speed, scale):
    shapes = {}
    first = last = None
    reachable = None
    lost_at = None
    widths = []
    last_time = None
    for line in path.open(encoding="utf-8"):
        frame = json.loads(line)
        if "id" in frame:
            shapes[frame["id"]] = [np.asarray(poly) for poly in frame["polygons"]]
            continue
        blocked = draw_frame(frame, shapes, scale)
        right = round((frame["right"] - 66) * scale)
        live = np.any(blocked[:, :right + 1])
        blocked[:, right:] = 1
        blocked[:, [0, -1]] = blocked[[0, -1], :] = 1
        safe = 1 - blocked
        distances = cv2.distanceTransform(safe, cv2.DIST_L2, cv2.DIST_MASK_PRECISE) / scale
        if live:
            if first is None:
                first = len(widths)
            last = len(widths)
        widths.append(float(distances.max() * 2))
        # Keep the full 6px probe clear, including grid rounding. Do not teleport between disconnected openings.
        clear = (distances >= 3 + math.sqrt(2) / scale).astype(np.uint8)
        if last_time is None:
            reachable = np.zeros_like(clear)
            x, y = point(frame["x"], frame["y"], scale)
            reachable[np.clip(y, 0, clear.shape[0] - 1), np.clip(x, 0, clear.shape[1] - 1)] = 1
        else:
            radius = min(speed * max(0, frame["t"] - last_time) * scale, 5 * scale)
            r = math.floor(radius + 1e-6)
            dy, dx = np.ogrid[-r:r + 1, -r:r + 1]
            kernel = (dx * dx + dy * dy <= radius * radius + 1e-5).astype(np.uint8)
            reachable = cv2.dilate(reachable, kernel)
        reachable &= clear
        if lost_at is None and not np.any(reachable):
            lost_at = frame["t"]
        last_time = frame["t"]
    active = np.asarray(widths[first:last + 1] if last is not None else [])
    near = np.abs(active - target) <= 1
    return dict(trace=path.name, scale=scale, target=target, speed=speed, frames=len(active),
                fraction_near_target=float(np.mean(near)) if len(active) else None,
                fraction_above_sprite=float(np.mean(active >= 15.2)) if len(active) else None,
                maximum_empty_diameter_px=float(np.max(active)) if len(active) else None,
                sampled_6px_route_found=lost_at is None and last is not None, lost_at=lost_at)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Measure the largest empty diameter at 60 Hz and replay a normal-speed 6px probe on recorded attack geometry.")
    parser.add_argument("trace", type=Path)
    parser.add_argument("--target", type=float, required=True)
    parser.add_argument("--speed", type=float, default=60)
    parser.add_argument("--scale", type=int, default=2)
    args = parser.parse_args()
    cv2.setNumThreads(1)
    print(json.dumps(measure(args.trace, args.target, args.speed, args.scale)))
