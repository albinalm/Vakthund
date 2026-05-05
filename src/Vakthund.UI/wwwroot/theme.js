function setDocumentTitle(title) {
    document.title = title;
}

(function () {
    const mq = window.matchMedia('(prefers-color-scheme: dark)');

    function apply(dark) {
        document.cookie = 'theme=' + (dark ? 'dark' : 'light') + ';path=/;max-age=31536000;samesite=strict';
        document.documentElement.classList.toggle('dark', dark);
    }

    apply(mq.matches);
    mq.addEventListener('change', e => apply(e.matches));
})();
