// WEBOCRV2 — ripple effect (water-drop on click)
// Auto-attaches to anything with .ripple, .pill, .btn-aurora, .btn-violet,
// .btn-ghost, .menu-item, .chip, .m-row, .surface--hoverable.
(function () {
  const SEL = '.ripple, .pill, .btn-aurora, .btn-violet, .btn-ghost, .menu-item, .chip, .m-row, .surface--hoverable, .seg button';
  function spawn(e) {
    const host = e.target.closest(SEL);
    if (!host) return;
    if (getComputedStyle(host).position === 'static') host.style.position = 'relative';
    host.style.overflow = host.style.overflow || 'hidden';
    const r = host.getBoundingClientRect();
    const x = (e.clientX ?? r.left + r.width / 2) - r.left;
    const y = (e.clientY ?? r.top + r.height / 2) - r.top;
    const size = Math.max(r.width, r.height);
    const drop = document.createElement('span');
    const isLight = host.matches('.pill--muted, .pill--targeted, .btn-ghost, .seg button:not(.active), .chip:not(.on)');
    drop.className = 'ripple-drop' + (isLight ? ' dark' : '');
    drop.style.left = x + 'px';
    drop.style.top = y + 'px';
    drop.style.width = drop.style.height = size + 'px';
    host.appendChild(drop);
    setTimeout(() => drop.remove(), 700);
  }
  document.addEventListener('pointerdown', spawn);
})();
