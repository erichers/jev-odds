import type * as T from 'three';

type Three = typeof import('three');

/**
 * R6 fallback still drawn from the live scene: every mesh triangle, line and grid of `scene` is projected through the same
 * PerspectiveCamera (Vector3.project needs no WebGL context) and painted into `svg` back to front. Used when WebGL cannot
 * start and while the WebGL context is lost, so the still is the same subject from the same camera, in the same colors.
 * Shading follows three's Lambert term (ambient + directional N.L, divided by pi) on the material color and vertex colors.
 */
export function paintStill(
  THREE: Three,
  svg: SVGSVGElement,
  scene: T.Scene,
  camera: T.PerspectiveCamera,
  width: number,
  height: number,
  opts: { background?: string | null; lineWidth?: number } = {},
): void {
  const NS = 'http://www.w3.org/2000/svg';
  const w = Math.max(1, Math.round(width));
  const h = Math.max(1, Math.round(height));
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
  scene.updateMatrixWorld(true);
  camera.updateMatrixWorld(true);
  const view = camera.matrixWorldInverse;

  const ambient = new THREE.Color(0, 0, 0);
  const lights: { dir: T.Vector3; color: T.Color }[] = [];
  scene.traverse((o: any) => {
    if (o.visible === false) return;
    if (o.isAmbientLight) ambient.add(o.color.clone().multiplyScalar(o.intensity));
    else if (o.isHemisphereLight) ambient.add(o.color.clone().lerp(o.groundColor, 0.5).multiplyScalar(o.intensity));
    else if (o.isDirectionalLight) {
      const p = new THREE.Vector3().setFromMatrixPosition(o.matrixWorld);
      const t = new THREE.Vector3().setFromMatrixPosition(o.target.matrixWorld);
      lights.push({ dir: p.sub(t).normalize(), color: o.color.clone().multiplyScalar(o.intensity) });
    }
  });

  type Prim = { z: number; poly: boolean; pts: string; color: string; opacity: number; width?: number };
  const prims: Prim[] = [];
  const camPos = new THREE.Vector3().setFromMatrixPosition(camera.matrixWorld);
  const a = new THREE.Vector3(), b = new THREE.Vector3(), c = new THREE.Vector3();
  const na = new THREE.Vector3(), nb = new THREE.Vector3(), nc = new THREE.Vector3();
  const n = new THREE.Vector3(), mid = new THREE.Vector3(), tmp = new THREE.Vector3(), tc = new THREE.Vector3();
  const col = new THREE.Color(), vc = new THREE.Color(), lit = new THREE.Color();
  const screen = (v: T.Vector3) => {
    tmp.copy(v).project(camera);
    return `${(((tmp.x + 1) / 2) * w).toFixed(1)},${(((1 - tmp.y) / 2) * h).toFixed(1)}`;
  };
  const depth = (v: T.Vector3) => -tmp.copy(v).applyMatrix4(view).z;
  const behind = (v: T.Vector3) => depth(v) <= camera.near;

  scene.traverse((o: any) => {
    if (!o.visible || !o.geometry || !o.material || Array.isArray(o.material)) return;
    const geo = o.geometry as T.BufferGeometry;
    const pos = geo.getAttribute('position');
    if (!pos) return;
    const mat = o.material as any;
    if (mat.visible === false) return;
    const colors = mat.vertexColors ? geo.getAttribute('color') : undefined;
    const opacity = mat.transparent ? mat.opacity ?? 1 : 1;
    const world = o.matrixWorld as T.Matrix4;
    const vertex = (i: number, out: T.Vector3) => out.fromBufferAttribute(pos as T.BufferAttribute, i).applyMatrix4(world);
    const tint = (i: number, out: T.Color) => {
      out.copy(mat.color ?? col.setRGB(1, 1, 1));
      if (colors) out.multiply(vc.setRGB(colors.getX(i), colors.getY(i), colors.getZ(i)));
      return out;
    };

    if (o.isMesh) {
      const index = geo.getIndex();
      const count = index ? index.count : pos.count;
      const normals = geo.getAttribute('normal');
      const nm = new THREE.Matrix3().getNormalMatrix(world);
      const basic = !!mat.isMeshBasicMaterial;
      const metal = mat.metalness ?? 0;
      const quads = !!(o.userData?.['stillQuads'] || geo.userData?.['stillQuads']);
      const quadColor = col.clone();
      for (let k = 0; k + 2 < count; k += 3) {
        const i0 = index ? index.getX(k) : k, i1 = index ? index.getX(k + 1) : k + 1, i2 = index ? index.getX(k + 2) : k + 2;
        if (quads && index && k % 6 === 0 && k + 5 < count) {
          // grid meshes (PlaneGeometry order): both triangles of a quad take the average of its 4 corners, so a color
          // edge (the lime band) runs straight along the quad row instead of as alternating teeth
          const ids = new Set([i0, i1, i2, index.getX(k + 3), index.getX(k + 4), index.getX(k + 5)]);
          quadColor.setRGB(0, 0, 0);
          for (const id of ids) quadColor.add(tint(id, vc.clone()));
          quadColor.multiplyScalar(1 / ids.size);
        }
        vertex(i0, a); vertex(i1, b); vertex(i2, c);
        if (behind(a) || behind(b) || behind(c)) continue;
        mid.copy(a).add(b).add(c).divideScalar(3);
        const face = n.copy(b).sub(a).cross(nc.copy(c).sub(a)).normalize();
        const toCam = tc.copy(camPos).sub(mid);
        const facing = face.dot(toCam) >= 0;
        if (!facing && mat.side === THREE.FrontSide) continue;
        if (facing && mat.side === THREE.BackSide) continue;
        if (normals) {
          na.fromBufferAttribute(normals as T.BufferAttribute, i0);
          nb.fromBufferAttribute(normals as T.BufferAttribute, i1);
          nc.fromBufferAttribute(normals as T.BufferAttribute, i2);
          n.copy(na).add(nb).add(nc).applyMatrix3(nm).normalize();
          if (n.dot(toCam) < 0) n.negate();
        } else if (!facing) n.negate();
        if (quads && index) col.copy(quadColor);
        else {
          tint(i0, col);
          col.add(tint(i1, vc.clone())).add(tint(i2, vc.clone())).multiplyScalar(1 / 3);
        }
        if (basic) lit.copy(col);
        else {
          const irr = ambient.clone();
          for (const l of lights) irr.add(l.color.clone().multiplyScalar(Math.max(0, n.dot(l.dir))));
          lit.copy(col).multiplyScalar(1 - metal).multiply(irr).multiplyScalar(1 / Math.PI);
          if (mat.emissive) lit.add(mat.emissive.clone().multiplyScalar(mat.emissiveIntensity ?? 1));
          // a little specular sheen toward the key light, as the PBR materials show
          if (lights[0]) {
            const half = lights[0].dir.clone().add(toCam.normalize()).normalize();
            const spec = Math.pow(Math.max(0, n.dot(half)), 24) * 0.12 * (1 - (mat.roughness ?? 1) * 0.6);
            lit.r += spec; lit.g += spec; lit.b += spec;
          }
        }
        lit.r = Math.min(1, lit.r); lit.g = Math.min(1, lit.g); lit.b = Math.min(1, lit.b);
        prims.push({ z: depth(mid), poly: true, pts: `${screen(a)} ${screen(b)} ${screen(c)}`, color: '#' + lit.getHexString(), opacity });
      }
      return;
    }

    if (o.isLine) {
      const segs = o.isLineSegments;
      const loop = o.isLineLoop;
      const index = geo.getIndex();
      const count = index ? index.count : pos.count;
      const at = (k: number) => (index ? index.getX(k) : k);
      const step = segs ? 2 : 1;
      const last = loop ? count : count - 1;
      for (let k = 0; k < last; k += step) {
        const i0 = at(k), i1 = at((k + 1) % count);
        vertex(i0, na); vertex(i1, nb);
        if (behind(na) || behind(nb)) continue;
        tint(i0, col);
        col.add(tint(i1, vc.clone())).multiplyScalar(0.5);
        const hex = '#' + col.getHexString();
        // long segments (grid lines) are cut into short pieces so each piece sorts against the faces it passes under
        const pieces = Math.max(1, Math.ceil(na.distanceTo(nb) / 0.12));
        for (let q = 0; q < pieces; q++) {
          a.copy(na).lerp(nb, q / pieces);
          b.copy(na).lerp(nb, (q + 1) / pieces);
          mid.copy(a).add(b).multiplyScalar(0.5);
          // lines draw over the faces they sit on (they are a few hundredths above the surface in every scene)
          prims.push({ z: depth(mid) - 0.03, poly: false, pts: `${screen(a)} ${screen(b)}`, color: hex, opacity, width: mat.userData?.stillWidth });
        }
      }
    }
  });

  prims.sort((p, q) => q.z - p.z);
  while (svg.firstChild) svg.removeChild(svg.firstChild);
  svg.setAttribute('viewBox', `0 0 ${w} ${h}`);
  svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');
  const frag = document.createDocumentFragment();
  if (opts.background) {
    const r = document.createElementNS(NS, 'rect');
    r.setAttribute('width', String(w));
    r.setAttribute('height', String(h));
    r.setAttribute('fill', opts.background);
    frag.appendChild(r);
  }
  const lw = String(opts.lineWidth ?? 1);
  for (const p of prims) {
    const el = document.createElementNS(NS, p.poly ? 'polygon' : 'polyline');
    el.setAttribute('points', p.pts);
    if (p.poly) {
      el.setAttribute('fill', p.color);
      if (p.opacity < 1) el.setAttribute('fill-opacity', p.opacity.toFixed(2));
      else {
        // hide the hairline seams between neighbouring opaque faces
        el.setAttribute('stroke', p.color);
        el.setAttribute('stroke-width', '1');
        el.setAttribute('stroke-linejoin', 'round');
      }
    } else {
      el.setAttribute('fill', 'none');
      el.setAttribute('stroke', p.color);
      el.setAttribute('stroke-width', p.width ? String(p.width) : lw);
      el.setAttribute('stroke-linecap', 'round');
      if (p.opacity < 1) el.setAttribute('stroke-opacity', p.opacity.toFixed(2));
    }
    frag.appendChild(el);
  }
  svg.appendChild(frag);
  svg.setAttribute('data-still', 'scene');
}
