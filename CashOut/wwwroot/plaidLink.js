// plaidLink.js
// Lazy-loads the Plaid Link SDK on first use so it is only fetched when the
// user visits the Accounts page, not on every page of the app.

window.cashoutPlaid = {
    handler: null,
    _sdkLoaded: false,

    _loadSdk: function (callback) {
        if (window.cashoutPlaid._sdkLoaded && typeof window.Plaid !== 'undefined') {
            callback();
            return;
        }

        var script = document.createElement('script');
        script.src = 'https://cdn.plaid.com/link/v2/stable/link-initialize.js';
        script.onload = function () {
            window.cashoutPlaid._sdkLoaded = true;
            console.log('[Plaid] SDK loaded');
            callback();
        };
        script.onerror = function () {
            console.error('[Plaid] Failed to load SDK script');
        };
        document.head.appendChild(script);
    },

    open: function (linkToken, dotNetRef) {
        console.log('[Plaid] open() called, linkToken prefix:',
            linkToken ? linkToken.substring(0, 20) + '...' : 'NULL');

        window.cashoutPlaid._loadSdk(function () {
            var attempts = 0;
            var maxAttempts = 50;

            var tryOpen = function () {
                attempts++;
                if (typeof window.Plaid === 'undefined') {
                    if (attempts >= maxAttempts) {
                        var msg = 'Plaid SDK failed to initialise after 5s. Check network/CSP.';
                        console.error('[Plaid]', msg);
                        dotNetRef.invokeMethodAsync('OnPlaidError', msg);
                        return;
                    }
                    setTimeout(tryOpen, 100);
                    return;
                }

                console.log('[Plaid] window.Plaid ready, creating handler...');
                try {
                    window.cashoutPlaid.handler = window.Plaid.create({
                        token: linkToken,
                        onSuccess: function (public_token, metadata) {
                            console.log('[Plaid] onSuccess');
                            dotNetRef.invokeMethodAsync('OnPlaidSuccess', public_token);
                        },
                        onExit: function (err, metadata) {
                            console.log('[Plaid] onExit, err:', err);
                            dotNetRef.invokeMethodAsync('OnPlaidExit');
                        },
                        onEvent: function (eventName) {
                            console.log('[Plaid] event:', eventName);
                        }
                    });

                    window.cashoutPlaid.handler.open();
                    console.log('[Plaid] handler.open() called');
                } catch (e) {
                    console.error('[Plaid] exception during create/open:', e);
                    dotNetRef.invokeMethodAsync('OnPlaidError',
                        'Failed to open Plaid Link: ' + e.message);
                }
            };

            tryOpen();
        });
    },

    destroy: function () {
        if (window.cashoutPlaid.handler) {
            try { window.cashoutPlaid.handler.destroy(); } catch (e) { }
            window.cashoutPlaid.handler = null;
        }
    }
};

console.log('[Plaid] plaidLink.js loaded');