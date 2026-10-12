// Runs before every spec file: the specs check Czech texts, while jsdom's browser says "en-US".
// Remembered choice wins over the browser language (core/i18n.ts detectLang) – so the tests run in Czech.
localStorage.setItem('jobscout.lang', 'cs');
