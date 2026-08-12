// Sourced from https://github.com/gzuidhof/coi-serviceworker
if (typeof window !== 'undefined' && 'serviceWorker' in navigator) {
  navigator.serviceWorker.register('./coi-serviceworker.js').then((registration) => {
    registration.addEventListener('updatefound', () => {
      window.location.reload();
    });
  });
                                  }
