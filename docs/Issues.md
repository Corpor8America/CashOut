## Errors
Errors on every page like "Failed to load accounts: Response status code does not indicate success: 500 (Internal Server Error)." on initial load.  None of the buttons work

After running docker commands and launching app localhost:8080 shows the text "page not found"

various web console errors and warkning 
```
Failed to load resource: the server responded with a status of 404 (Not Found)
~/plaidLink.js:1  Failed to load resource: the server responded with a status of 404 (Not Found)
blazor.server.js:1 [2026-06-08T19:56:49.273Z] Information: Normalizing '_blazor' to 'http://localhost:8080/_blazor'.
blazor.server.js:1 [2026-06-08T19:56:49.302Z] Information: WebSocket connected to ws://localhost:8080/_blazor?id=OeJqaYAP0wao34cURcL0Ww.
~/plaidLink.js:1  Failed to load resource: the server responded with a status of 404 (Not Found)
VM20 link-initialize.js:1 Warning: The Plaid link-initialize.js script was embedded more than once. This is an unsupported configuration and may lead to unpredictable behavior. Please ensure Plaid Link is embedded only once per page.
3819 @ VM20 link-initialize.js:1
transactions:1 Uncaught (in promise) Error: A listener indicated an asynchronous response by returning true, but the message channel closed before a response was received
3content-script.js:1 cornhusk, shared-service, error: TypeError: Failed to construct 'URL': Invalid URLInvalid url: 
extractOriginPath @ content-script.js:1

AuthenticationService.js:1  Failed to load resource: the server responded with a status of 404 (Not Found)
blazor.server.js:1 cornhusk, shared-service, error: TypeError: Failed to construct 'URL': Invalid URLInvalid url: 
extractOriginPath @ content-script.js:1
fingerprintPage @ content-script.js:1
observeCheckoutMutations @ content-script.js:1
childList
j @ blazor.server.js:1
j @ blazor.server.js:1
j @ blazor.server.js:1
applyEdits @ blazor.server.js:1
updateComponent @ blazor.server.js:1
(anonymous) @ blazor.server.js:1
processBatch @ blazor.server.js:1
(anonymous) @ blazor.server.js:1
_invokeClientMethod @ blazor.server.js:1
_processIncomingData @ blazor.server.js:1
(anonymous) @ blazor.server.js:1
(anonymous) @ blazor.server.js:1
blazor.server.js:1  GET http://localhost:8080/~/plaidLink.js net::ERR_ABORTED 404 (Not Found)
Y @ blazor.server.js:1
Y @ blazor.server.js:1
Y @ blazor.server.js:1
H @ blazor.server.js:1
insertMarkup @ blazor.server.js:1
insertFrame @ blazor.server.js:1
applyEdits @ blazor.server.js:1
updateComponent @ blazor.server.js:1
(anonymous) @ blazor.server.js:1
processBatch @ blazor.server.js:1
(anonymous) @ blazor.server.js:1
_invokeClientMethod @ blazor.server.js:1
_processIncomingData @ blazor.server.js:1
(anonymous) @ blazor.server.js:1
(anonymous) @ blazor.server.js:1
link-initialize.js:1 Warning: The Plaid link-initialize.js script was embedded more than once. This is an unsupported configuration and may lead to unpredictable behavior. Please ensure Plaid Link is embedded only once per page.
```