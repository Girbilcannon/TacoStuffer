(() => {
  const navToggle = document.querySelector('.nav-toggle');
  const scrim = document.querySelector('.scrim');
  const navClose = document.querySelector('.nav-close');
  function setNavigation(open) {
    document.body.classList.toggle('menu-open', open);
    navToggle.setAttribute('aria-expanded', String(open));
    scrim.hidden = !open;
    if (open) navClose.focus();
  }
  navToggle.addEventListener('click', () => setNavigation(!document.body.classList.contains('menu-open')));
  navClose.addEventListener('click', () => { setNavigation(false); navToggle.focus(); });
  scrim.addEventListener('click', () => { setNavigation(false); navToggle.focus(); });
  const links = [...document.querySelectorAll('.sidebar nav a')];
  links.forEach(link => link.addEventListener('click', () => setNavigation(false)));
  document.querySelector('.brand').addEventListener('click', () => setNavigation(false));
  if ('IntersectionObserver' in window) {
    const observer = new IntersectionObserver(entries => {
      for (const entry of entries) if (entry.isIntersecting) {
        links.forEach(link => {
          if (link.hash === '#' + entry.target.id) link.setAttribute('aria-current', 'location');
          else link.removeAttribute('aria-current');
        });
      }
    }, { rootMargin: '-8% 0px -75% 0px' });
    document.querySelectorAll('main section').forEach(section => observer.observe(section));
  }
  const viewer = document.querySelector('#image-viewer');
  const image = document.querySelector('#viewer-image');
  const caption = document.querySelector('#viewer-caption');
  const scroll = document.querySelector('.viewer-scroll');
  const zoom = document.querySelector('#viewer-zoom');
  let trigger;
  document.querySelectorAll('[data-enlarge]').forEach(link => link.addEventListener('click', event => {
    if (typeof viewer.showModal !== 'function') return; // Plain links remain a no-script fallback.
    event.preventDefault();
    trigger = link;
    image.src = link.href;
    image.alt = link.querySelector('img').alt;
    caption.textContent = image.alt;
    scroll.classList.remove('actual');
    zoom.setAttribute('aria-pressed', 'false'); zoom.textContent = 'Actual size';
    viewer.showModal();
  }));
  zoom.addEventListener('click', () => {
    const actual = scroll.classList.toggle('actual');
    zoom.setAttribute('aria-pressed', String(actual)); zoom.textContent = actual ? 'Fit image' : 'Actual size';
  });
  document.querySelector('#viewer-close').addEventListener('click', () => viewer.close());
  viewer.addEventListener('click', event => { if (event.target === viewer) viewer.close(); });
  viewer.addEventListener('close', () => { image.removeAttribute('src'); trigger?.focus(); });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && document.body.classList.contains('menu-open') && !viewer.open) {
      setNavigation(false); navToggle.focus();
    }
  });
})();
