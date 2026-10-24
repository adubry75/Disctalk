// DisctalkClient UI helpers. Loaded as an ES module through IJSRuntime.

let delegationInstalled = false;

/** Spoilers reveal on click/Enter; channel mentions navigate. One document-level listener for all messages. */
export function installDelegation(dotnet) {
    window.__disctalk = dotnet;
    if (delegationInstalled) return;
    delegationInstalled = true;
    document.addEventListener('click', e => {
        const spoiler = e.target.closest?.('.md-spoiler');
        if (spoiler && !spoiler.classList.contains('revealed')) {
            spoiler.classList.add('revealed');
            e.preventDefault();
            return;
        }
        const channel = e.target.closest?.('[data-channel-id]');
        if (channel && window.__disctalk) {
            window.__disctalk.invokeMethodAsync('OnChannelMentionClicked', channel.dataset.channelId);
        }
    });
    document.addEventListener('keydown', e => {
        if (e.key === 'Enter' && e.target.classList?.contains('md-spoiler')) e.target.classList.add('revealed');
    });
}

/**
 * The message scroller uses flex-direction: column-reverse, so the browser keeps it pinned to the newest message
 * and prepending history doesn't move the view. A sentinel at the top triggers loading older messages.
 */
export function observeTop(scroller, sentinel, dotnet) {
    const observer = new IntersectionObserver(entries => {
        if (entries.some(x => x.isIntersecting)) dotnet.invokeMethodAsync('OnReachedTop');
    }, { root: scroller, rootMargin: '400px 0px 0px 0px' });
    observer.observe(sentinel);
    return { dispose: () => observer.disconnect() };
}

export function scrollToBottom(scroller) {
    if (scroller) scroller.scrollTop = 0; // column-reverse: 0 is the bottom
}

/** Composer: Enter sends, Shift+Enter is a newline, Up on empty edits the last own message, Escape cancels. */
export function attachComposer(textarea, dotnet) {
    const resize = () => {
        textarea.style.height = 'auto';
        textarea.style.height = Math.min(textarea.scrollHeight, window.innerHeight * 0.4) + 'px';
    };
    const onKey = e => {
        if (e.isComposing) return;
        if (e.key === 'Enter' && !e.shiftKey && !textarea.dataset.mobile) {
            e.preventDefault();
            dotnet.invokeMethodAsync('SubmitFromJs', textarea.value);
        } else if (e.key === 'ArrowUp' && textarea.value === '') {
            e.preventDefault();
            dotnet.invokeMethodAsync('EditLastFromJs');
        } else if (e.key === 'Escape') {
            dotnet.invokeMethodAsync('CancelFromJs');
        }
    };
    textarea.addEventListener('keydown', onKey);
    textarea.addEventListener('input', resize);
    // Touch devices: Enter inserts a newline; the send button sends.
    if (window.matchMedia('(pointer: coarse)').matches) textarea.dataset.mobile = '1';
    resize();
    return {
        dispose: () => { textarea.removeEventListener('keydown', onKey); textarea.removeEventListener('input', resize); },
    };
}

export function setComposerText(textarea, text) {
    textarea.value = text;
    textarea.dispatchEvent(new Event('input'));
    textarea.focus();
    textarea.setSelectionRange(text.length, text.length);
}

export function insertAtCursor(textarea, text) {
    const start = textarea.selectionStart ?? textarea.value.length;
    const end = textarea.selectionEnd ?? start;
    textarea.value = textarea.value.slice(0, start) + text + textarea.value.slice(end);
    textarea.dispatchEvent(new Event('input'));
    textarea.focus();
    textarea.setSelectionRange(start + text.length, start + text.length);
    return textarea.value;
}

export function focus(el) { el?.focus(); }
