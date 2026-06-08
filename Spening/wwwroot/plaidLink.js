// Loaded once on the Accounts page.
// Blazor calls window.speningPlaid.open(linkToken, dotNetRef)
// On success, invokes dotNetRef.invokeMethodAsync('OnPlaidSuccess', publicToken)
// On exit/cancel, invokes dotNetRef.invokeMethodAsync('OnPlaidExit')

window.speningPlaid = {
    handler: null,

    open: function (linkToken, dotNetRef) {
        if (!window.Plaid) {
            console.error('Plaid Link SDK not loaded.');
            return;
        }

        this.handler = window.Plaid.create({
            token: linkToken,
            onSuccess: function (public_token, metadata) {
                dotNetRef.invokeMethodAsync('OnPlaidSuccess', public_token);
            },
            onExit: function (err, metadata) {
                dotNetRef.invokeMethodAsync('OnPlaidExit');
            }
        });

        this.handler.open();
    }
};
