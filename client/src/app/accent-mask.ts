/**
 * Accent-material mask for the design checker (RULING-checker-hue-v22.md, app side contract): canvas.__accentMask()
 * renders the scene once with the same camera at the canvas CSS size, accent materials (userData.token === 'accent')
 * white and everything else black with depth kept, and returns a Uint8Array(cssW * cssH), 1 = accent material.
 * Only called by QA tooling; it never runs during normal use.
 */
export function installAccentMask(THREE: any, renderer: any, scene: any, camera: any): void {
  const canvas = renderer.domElement as HTMLCanvasElement & { __accentMask?: () => Uint8Array };
  canvas.__accentMask = () => {
    const w = Math.max(1, Math.round(canvas.clientWidth));
    const h = Math.max(1, Math.round(canvas.clientHeight));
    const target = new THREE.WebGLRenderTarget(w, h);
    const mats = {
      meshOn: new THREE.MeshBasicMaterial({ color: 0xffffff, side: THREE.DoubleSide }),
      meshOff: new THREE.MeshBasicMaterial({ color: 0x000000, side: THREE.DoubleSide }),
      lineOn: new THREE.LineBasicMaterial({ color: 0xffffff }),
      lineOff: new THREE.LineBasicMaterial({ color: 0x000000 }),
    };
    const saved = new Map<any, any>();
    scene.traverse((o: any) => {
      if (!o.material || Array.isArray(o.material)) return;
      saved.set(o, o.material);
      const on = o.material.userData?.token === 'accent';
      o.material = o.isLine ? (on ? mats.lineOn : mats.lineOff) : on ? mats.meshOn : mats.meshOff;
    });
    const background = scene.background;
    const clear = new THREE.Color();
    renderer.getClearColor(clear);
    const alpha = renderer.getClearAlpha();
    const prevTarget = renderer.getRenderTarget();
    const toneMapping = renderer.toneMapping;
    const pixels = new Uint8Array(w * h * 4);
    try {
      scene.background = null;
      renderer.toneMapping = THREE.NoToneMapping;
      renderer.setRenderTarget(target);
      renderer.setClearColor(0x000000, 1);
      renderer.clear();
      renderer.render(scene, camera);
      renderer.readRenderTargetPixels(target, 0, 0, w, h, pixels);
    } finally {
      for (const [o, m] of saved) o.material = m;
      scene.background = background;
      renderer.setRenderTarget(prevTarget);
      renderer.setClearColor(clear, alpha);
      renderer.toneMapping = toneMapping;
      target.dispose();
      for (const m of Object.values(mats)) (m as any).dispose();
      renderer.render(scene, camera);
    }
    const out = new Uint8Array(w * h);
    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) out[(h - 1 - y) * w + x] = pixels[(y * w + x) * 4] > 127 ? 1 : 0;
    }
    return out;
  };
}
