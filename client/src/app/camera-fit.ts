import type { PerspectiveCamera, Vector3 } from 'three';

/**
 * Resting camera: keep the view direction, then move along it and re-aim so the projected subject is centered and
 * fills `margin` of the frame on its limiting axis. Used for the live view and the reduced-motion still alike, so the
 * still is the live view at rest.
 */
export function fitCamera(camera: PerspectiveCamera, target: Vector3, points: Vector3[], margin = 0.94): void {
  if (!points.length) return;
  const dir = camera.position.clone().sub(target).normalize();
  const p = points[0].clone();
  const extent = (d: number) => {
    camera.position.copy(target).addScaledVector(dir, d);
    camera.lookAt(target);
    camera.updateMatrixWorld(true);
    let x0 = Infinity, x1 = -Infinity, y0 = Infinity, y1 = -Infinity;
    for (const q of points) {
      p.copy(q).project(camera);
      if (p.x < x0) x0 = p.x;
      if (p.x > x1) x1 = p.x;
      if (p.y < y0) y0 = p.y;
      if (p.y > y1) y1 = p.y;
    }
    return { x0, x1, y0, y1 };
  };
  for (let pass = 0; pass < 4; pass++) {
    let lo = 0.5, hi = 60;
    for (let i = 0; i < 40; i++) {
      const mid = (lo + hi) / 2;
      const e = extent(mid);
      const span = Math.max((e.x1 - e.x0) / 2, (e.y1 - e.y0) / 2);
      if (span > margin) lo = mid; else hi = mid;
    }
    const e = extent(hi);
    const cx = (e.x0 + e.x1) / 2;
    const cy = (e.y0 + e.y1) / 2;
    if (Math.abs(cx) < 0.002 && Math.abs(cy) < 0.002) break;
    // shift the aim point so the subject's box is centered in the frame
    const dist = camera.position.distanceTo(target);
    const halfH = Math.tan((camera.fov * Math.PI) / 360) * dist;
    const right = camera.matrixWorld.elements.slice(0, 3);
    const up = camera.matrixWorld.elements.slice(4, 7);
    const sx = cx * halfH * camera.aspect;
    const sy = cy * halfH;
    target.x += right[0] * sx + up[0] * sy;
    target.y += right[1] * sx + up[1] * sy;
    target.z += right[2] * sx + up[2] * sy;
  }
}

/** Aspect (width / height) of the subject's projected box from a given camera, at any distance. */
function drawnAspect(camera: PerspectiveCamera, target: Vector3, points: Vector3[]): number {
  const p = points[0].clone();
  let x0 = Infinity, x1 = -Infinity, y0 = Infinity, y1 = -Infinity;
  camera.lookAt(target);
  camera.updateMatrixWorld(true);
  for (const q of points) {
    p.copy(q).project(camera);
    x0 = Math.min(x0, p.x); x1 = Math.max(x1, p.x); y0 = Math.min(y0, p.y); y1 = Math.max(y1, p.y);
  }
  return ((x1 - x0) * camera.aspect) / Math.max(y1 - y0, 1e-6);
}

/**
 * Resting camera for a frame: keep the azimuth, pick the elevation (within [minEl, maxEl] degrees) whose drawing has
 * the frame's aspect, then fit. A tall phone frame gets a higher camera and a wide frame a lower one, so the subject
 * fills the frame at every width instead of leaving bands of empty space.
 */
export function fitResting(
  camera: PerspectiveCamera,
  target: Vector3,
  points: Vector3[],
  opts: { azimuth: number; minEl: number; maxEl: number; margin?: number; distance?: number },
): void {
  let d = opts.distance ?? 8;
  const place = (deg: number) => {
    const e = (deg * Math.PI) / 180;
    camera.position.set(
      target.x + Math.sin(opts.azimuth) * Math.cos(e) * d,
      target.y + Math.sin(e) * d,
      target.z + Math.cos(opts.azimuth) * Math.cos(e) * d,
    );
  };
  // Two rounds: the second searches at the fitted distance, since perspective changes the drawn aspect.
  for (let round = 0; round < 2; round++) {
    searchElevation(camera, target, points, opts, place);
    fitCamera(camera, target, points, opts.margin ?? 0.94);
    d = camera.position.distanceTo(target);
  }
}

function searchElevation(
  camera: PerspectiveCamera,
  target: Vector3,
  points: Vector3[],
  opts: { minEl: number; maxEl: number },
  place: (deg: number) => void,
): void {
  // drawn aspect falls as the camera rises
  let lo = opts.minEl, hi = opts.maxEl;
  place(lo);
  if (drawnAspect(camera, target, points) <= camera.aspect) hi = lo;
  else {
    place(hi);
    if (drawnAspect(camera, target, points) >= camera.aspect) lo = hi;
  }
  for (let i = 0; i < 30 && hi - lo > 0.05; i++) {
    const mid = (lo + hi) / 2;
    place(mid);
    if (drawnAspect(camera, target, points) > camera.aspect) lo = mid; else hi = mid;
  }
  place((lo + hi) / 2);
}
