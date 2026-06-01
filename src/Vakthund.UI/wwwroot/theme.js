function setDocumentTitle(title) {
    document.title = title;
}

function highlightCodeElement(element) {
    if (!element || !window.hljs) {
        return;
    }

    element.removeAttribute('data-highlighted');
    window.hljs.highlightElement(element);
}

async function copyToClipboard(text) {
    if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
        return;
    }

    const textArea = document.createElement('textarea');
    textArea.value = text;
    textArea.setAttribute('readonly', '');
    textArea.style.position = 'fixed';
    textArea.style.top = '-1000px';
    document.body.appendChild(textArea);
    textArea.select();

    try {
        if (!document.execCommand('copy')) {
            throw new Error('Copy command was rejected.');
        }
    } finally {
        document.body.removeChild(textArea);
    }
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
