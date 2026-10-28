# Cart, order and payment boundaries

This is an educational application. These controls and fixture checks do not certify a deployed payment system.

## Identity, ownership and allowed input

- Cart mutations require the stored cart owner. Creating cart entries accepts only product ID and bounded quantity; the product comes from the repository.
- Checkout accepts contact fields only. Owner, prices, totals, order/payment states, dates and provider IDs are generated from stored cart, user and product data. Quantities must be 1–1000; the existing quantity price tiers remain in effect.
- Customers may read only their own orders and initiate or verify only their own payments. Admin/Employee may read and administer orders, but may not pay on behalf of another owner.
- Administrative contact edits and shipments have separate input models. Order-list JSON exposes an explicit projection, not the Identity user entity; table text uses escaping renderers.
- MVC unsafe methods require antiforgery. Cart mutations and product-image deletion are POST operations. Confirmation GETs only display stored state. The shared AJAX token header is sent only to the same origin.

## Provider verification and transitions

Checkout uses persisted order lines, checked integer minor units and a stable per-order idempotency key. An existing matching open checkout is reused. Returning from the provider does not itself approve payment: the owner submits a protected verification POST.

Approval requires a paid checkout with matching session ID, order reference, USD currency and total, plus a succeeded matching PaymentIntent with matching amount and amount received. Failed/mismatched provider responses leave payment unapproved. A retry after local approval does not apply the transition or cart removal again. Only purchased product quantities are consumed from the owner's current cart.

State changes reread the order under a SQL Server update lock in a serializable transaction, after external provider I/O. A tracked order is refreshed after the lock. The total, session and permitted current state are rechecked before persistence. No provider call is held inside that database transaction.

Processing requires an approved order with approved or company-delayed payment terms. Shipping requires processing and those payment terms. Cancellation is limited to unpaid, unshipped orders: an open matching checkout must first be expired successfully. Paid or shipped orders require a separate reconciliation process; this sample does not automatically refund them.

## Verification and limits

Run the [regression suite](../tests/Authorization.Tests) and see [verification evidence](VERIFICATION.md). Tests exercise real MVC routing, binding, authentication and antiforgery in an in-process host, plus strict repository/payment mocks and Stripe SDK client mocks. They cover owner/staff boundaries, forged checkout fields, amount/currency/reference/intent mismatches, retry behavior, state guards, bootstrap failures and provider failures.

No original application host, SQL migration/seed, live Stripe, Facebook or email integration is started by these tests. SQL locking/rollback behavior and distributed races have not been integration-tested against SQL Server. The source still uses the original double-valued money model; conversion is checked at the provider boundary. Initial checkout form submissions are not durably deduplicated. Webhooks, durable reconciliation/outbox handling, retry policies for SQL deadlocks and a complete refund workflow remain outside this sample's verified scope.

Startup still applies the application's existing pending migrations when the original host is started. Review the database target before doing so. Bootstrap creates missing roles only; it does not create an account with a fixed credential. Historic values in Git history are not revoked by clearing source files.
