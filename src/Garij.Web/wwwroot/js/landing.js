/**
 * Garij Automotive Landing Page JavaScript
 * Features: Tachometer Preloader, Scroll-Reveal Animations, Smooth Navigation, Quick Lookup
 */

document.addEventListener('DOMContentLoaded', () => {
  // Public status lookup: give immediate, accessible feedback before navigation.
  const lookupForm = document.querySelector('[data-lookup-form]');
  if (lookupForm) {
    const lookupInput = lookupForm.querySelector('input[name="query"]');
    const lookupError = lookupForm.querySelector('#statusLookupError');
    const lookupButton = lookupForm.querySelector('[data-lookup-submit]');
    const lookupButtonText = lookupForm.querySelector('[data-lookup-submit-text]');

    lookupForm.addEventListener('submit', (event) => {
      const query = lookupInput ? lookupInput.value.trim() : '';
      if (!query) {
        event.preventDefault();
        if (lookupError) {
          lookupError.textContent = 'Enter a booking reference or license plate to track a service.';
          lookupError.hidden = false;
        }
        lookupInput?.focus();
        return;
      }

      if (lookupError) lookupError.hidden = true;
      lookupForm.setAttribute('aria-busy', 'true');
      if (lookupButton) {
        lookupButton.disabled = true;
        lookupButton.classList.add('is-loading');
      }
      if (lookupButtonText) lookupButtonText.textContent = 'Looking up…';
    });

    lookupInput?.addEventListener('input', () => {
      if (lookupError) lookupError.hidden = true;
    });
  }

  // 1. Lottie Preloader Handling
  const preloader = document.getElementById('page-preloader');
  const lottieContainer = document.getElementById('lottie-preloader');

  if (lottieContainer && window.lottie) {
    try {
      window.lottie.loadAnimation({
        container: lottieContainer,
        renderer: 'svg',
        loop: true,
        autoplay: true,
        path: '/animations/loading.json'
      });
    } catch (e) {
      console.warn('Lottie animation failed to load:', e);
    }
  }

  if (preloader) {
    const startTime = Date.now();
    const minDisplayMs = 2100; // Ensure cursive 'garij' animation completes its trace

    const hidePreloader = () => {
      const elapsed = Date.now() - startTime;
      const remaining = Math.max(0, minDisplayMs - elapsed);
      setTimeout(() => {
        preloader.classList.add('loaded');
        setTimeout(() => {
          preloader.style.display = 'none';
        }, 650);
      }, remaining);
    };

    if (document.readyState === 'complete') {
      hidePreloader();
    } else {
      window.addEventListener('load', hidePreloader);
      // Fallback safety timeout
      setTimeout(hidePreloader, 3200);
    }
  }

  // 2. Scroll Reveal Animations with IntersectionObserver
  const revealElements = document.querySelectorAll('.reveal, .reveal-left, .reveal-right');
  if ('IntersectionObserver' in window) {
    const revealObserver = new IntersectionObserver((entries, observer) => {
      entries.forEach(entry => {
        if (entry.isIntersecting) {
          entry.target.classList.add('active');
          observer.unobserve(entry.target);
        }
      });
    }, {
      root: null,
      threshold: 0.05,
      rootMargin: '0px 0px 50px 0px'
    });

    revealElements.forEach(el => revealObserver.observe(el));
  } else {
    // Fallback for older browsers
    revealElements.forEach(el => el.classList.add('active'));
  }

  // Safety: immediately reveal elements in or near initial viewport
  setTimeout(() => {
    revealElements.forEach(el => {
      const rect = el.getBoundingClientRect();
      if (rect.top <= (window.innerHeight || document.documentElement.clientHeight) + 150) {
        el.classList.add('active');
      }
    });
  }, 120);

  // Absolute safety fallback: guarantee all text is visible after 500ms
  setTimeout(() => {
    revealElements.forEach(el => el.classList.add('active'));
  }, 500);

  // 3. Smooth Scrolling for Internal Hash Anchors
  const smoothLinks = document.querySelectorAll('a[href^="#"], a[href^="/#"]');
  smoothLinks.forEach(link => {
    link.addEventListener('click', function (e) {
      let targetId = this.getAttribute('href');
      if (targetId.startsWith('/#')) {
        if (window.location.pathname === '/' || window.location.pathname === '') {
          targetId = targetId.substring(1);
        } else {
          return; // Allow natural navigation to /#hash
        }
      }
      if (targetId && targetId !== '#') {
        const targetElement = document.querySelector(targetId);
        if (targetElement) {
          e.preventDefault();

          // If inside offcanvas, close it smoothly before scrolling
          const isInsideOffcanvas = this.closest('.offcanvas');
          const delay = isInsideOffcanvas ? 280 : 0;

          setTimeout(() => {
            const navOffset = 80;
            const elementPosition = targetElement.getBoundingClientRect().top;
            const offsetPosition = elementPosition + window.pageYOffset - navOffset;

            window.scrollTo({
              top: offsetPosition,
              behavior: 'smooth'
            });
          }, delay);
          const navOffset = 85;
          const elementPosition = targetElement.getBoundingClientRect().top;
          const offsetPosition = elementPosition + window.pageYOffset - navOffset;

          window.scrollTo({
            top: offsetPosition,
            behavior: 'smooth'
          });
        }
      }
    });
  });

  // 4. Hero Multi-Slide Navigation Controls (Matching Mockup Arrows)
  const heroSlides = document.querySelectorAll('.hero-slide-item');
  const prevBtn = document.getElementById('heroPrevBtn');
  const nextBtn = document.getElementById('heroNextBtn');
  const indicators = document.querySelectorAll('.hero-slide-indicators .slide-indicator');
  let currentSlide = 0;
  let slideTimer = null;

  if (heroSlides.length > 0) {
    const showSlide = (index) => {
      if (index < 0) index = heroSlides.length - 1;
      if (index >= heroSlides.length) index = 0;
      currentSlide = index;

      heroSlides.forEach((slide, i) => {
        if (i === currentSlide) {
          slide.classList.add('active');
        } else {
          slide.classList.remove('active');
        }
      });

      indicators.forEach((dot, i) => {
        if (i === currentSlide) {
          dot.classList.add('active');
        } else {
          dot.classList.remove('active');
        }
      });
    };

    const nextSlide = () => showSlide(currentSlide + 1);
    const prevSlide = () => showSlide(currentSlide - 1);

    const resetSlideTimer = () => {
      if (slideTimer) clearInterval(slideTimer);
      slideTimer = setInterval(nextSlide, 8500); // Rotates every 8.5 seconds
    };

    if (nextBtn) {
      nextBtn.addEventListener('click', () => {
        nextSlide();
        resetSlideTimer();
      });
    }

    if (prevBtn) {
      prevBtn.addEventListener('click', () => {
        prevSlide();
        resetSlideTimer();
      });
    }

    indicators.forEach((dot, idx) => {
      dot.addEventListener('click', () => {
        showSlide(idx);
        resetSlideTimer();
      });
    });

    // Pause rotation on hover so user can read/click smoothly
    const heroSection = document.getElementById('hero');
    if (heroSection) {
      heroSection.addEventListener('mouseenter', () => {
        if (slideTimer) clearInterval(slideTimer);
      });
      heroSection.addEventListener('mouseleave', () => {
        resetSlideTimer();
      });
    }

    // Resume rotation when window/tab is active
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) {
        if (slideTimer) clearInterval(slideTimer);
      } else {
        resetSlideTimer();
      }
    });

    // Initialize auto rotation
    resetSlideTimer();

    // Keyboard arrow navigation
    window.addEventListener('keydown', (e) => {
      if (window.scrollY < window.innerHeight * 0.8) {
        if (e.key === 'ArrowLeft') {
          prevSlide();
          resetSlideTimer();
        } else if (e.key === 'ArrowRight') {
          nextSlide();
          resetSlideTimer();
        }
      }
    });
  }

  // 5. Active Navbar Link on Scroll Spy
  const sections = document.querySelectorAll('section[id]');
  const navLinks = document.querySelectorAll('.main-navbar-sticky .nav-link');

  const highlightNavLink = () => {
    let scrollY = window.pageYOffset;
    sections.forEach(section => {
      const sectionHeight = section.offsetHeight;
      const sectionTop = section.offsetTop - 120;
      const sectionId = section.getAttribute('id');

      if (scrollY > sectionTop && scrollY <= sectionTop + sectionHeight) {
        navLinks.forEach(link => {
          link.classList.remove('active');
          if (link.getAttribute('href') === `#${sectionId}` || link.getAttribute('href') === `/#${sectionId}`) {
            link.classList.add('active');
          }
        });
      }
    });
  };

  window.addEventListener('scroll', highlightNavLink, { passive: true });

  // 6. Auto-scroll on page load if URL contains hash (after preloader dismisses)
  const scrollToTarget = (targetSelector) => {
    try {
      const targetElement = document.querySelector(targetSelector);
      if (targetElement) {
        const navOffset = 85;
        const elementPosition = targetElement.getBoundingClientRect().top;
        const offsetPosition = elementPosition + window.pageYOffset - navOffset;
        window.scrollTo({
          top: offsetPosition,
          behavior: 'smooth'
        });
      }
    } catch (err) {
      // Invalid selector ignore
    }
  };

  if (window.location.hash && window.location.hash !== '#') {
    setTimeout(() => {
      scrollToTarget(window.location.hash);
    }, 600);
  } else if (window.location.search.includes('query=')) {
    setTimeout(() => {
      scrollToTarget('#status-tracker');
    }, 600);
  }
});
