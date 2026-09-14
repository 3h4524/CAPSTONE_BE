# API key connections

The feature follows ApiKeysController -> ApiKeyService -> IApiKeyRepository /
ApiKeyRepository. One service file contains all use cases; one repository handles
both display projections and credential writes. ListMetadataOwnedAsync selects only
non-secret columns with no tracking, while ListOwnedAsync loads owned entities for
mutations. ApiKeyCredentials remains the infrastructure adapter for encryption and
provider HTTP calls.

GET /api/api-keys returns the authenticated active Seller's non-deleted api_keys rows.
The endpoint rechecks account state and current roles in the database, filters by the
authenticated user ID, and sends Cache-Control: no-store. No caller-supplied owner ID
is accepted. The SQL projection excludes key_value_encrypted completely.

Apply scripts/sql/api-keys-view-metadata.sql before deploying this feature, then run
scripts/Scaffold-Database.ps1. The additive script preserves credentials and existing
rows. No new table is required. auth_type defaults to api_key; environment,
connected_account_name, last_checked_at and last_check_succeeded are nullable.
Provider display/category mapping is owned by ApiKeyService.

A disabled, expired (including expiry exactly at the current UTC time), or failed
credential is Reconnect. Connected requires an active credential, successful latest
check and recorded check timestamp. Other records are Needs attention, including
legacy records without check metadata. Last checked is the latest stored check,
never the time the list was fetched. Viewing the list does not validate credentials.

Core readiness requires at least one Connected provider for each of image generation
(Replicate), listing content (OpenAI), product publishing (Printify). Etsy listing publishing is optional for core readiness,
matching the supplied mockup. Duplicate credentials cannot substitute for a missing category.
All non-Connected rows count toward Needs attention.

Only exactly four suffix characters are displayed after ****; missing or malformed
suffixes are fully masked. OAuth rows display connected_account_name instead.
etsy_integrations and printify_integrations already exist but are separate integration
records; this API reads api_keys only, as required by the View API Keys use case.
The static API key flow persists credential metadata in api_keys. It does not
duplicate/import tokens from those shop-specific integration tables.

The frontend is /api-keys with seller navigation and breadcrumb. It scopes the query
cache to the signed-in user, discards inactive query data, refreshes on window focus
and every minute, and supports manual list refresh. Add/Edit/Delete/Validate mutations
invalidate apiKeyKeys.all, refreshing the table and summary counters.

GET /api/api-keys/providers returns the provider catalogue and permissions checklist.
POST /api/api-keys creates a connection; PUT /api/api-keys/{id} edits it;
DELETE /api/api-keys/{id} soft-deletes it and clears the encrypted credential and suffix.
POST /api/api-keys/{id}/validate checks the saved credential and records the result.
All endpoints recheck the active Seller account. Writes use owner-filtered lookups
and a PostgreSQL transaction advisory lock per owner, covering duplicate checks and
commit across application instances. Other writers of api_keys must use this same
repository lock to preserve the invariant. Existing duplicate rows are not rewritten.

Save requests contain provider (openai/replicate/printify), optional name (100 chars),
environment (Production/Sandbox/null), apiKey, and confirmed. apiKey is mandatory for
Add; null on Edit preserves the existing key. A provider cannot be changed on Edit.
Replacement keys must pass the connectivity check before any existing fields change.
Metadata-only edits preserve connection/check status. New keys record LastUsedAt and
LastCheckedAt at successful validation, as specified by the Add SRS.
Environment is a workspace label; it does not route traffic to a separate endpoint.

Connectivity probes use read-only HTTPS endpoints with Bearer authentication:
[OpenAI models](https://platform.openai.com/docs/api-reference/models),
[Replicate account](https://replicate.com/docs/reference/http#account.get), and
[Printify shops](https://developers.printify.com/#shops).
Redirects are disabled, credentials are not logged, and requests time out after 15s.
These probes verify access to the probe endpoint; they cannot prove every inference
or publishing permission. The checklist remains a Seller confirmation. No paid
generation or publishing side effects are triggered by validation.

Etsy is listed as unavailable in the key-entry catalogue because it requires OAuth.
The repository has no Etsy OAuth authorization, callback, refresh, or pipeline
implementation. Reconnect for OAuth rows remains disabled; deleting an api_keys row
does not revoke provider-side credentials or modify shop integration records.

Credentials use ASP.NET Core Data Protection, application name APCS and purpose
APCS.ApiKeys.v1. Keep its key ring persistent and shared by all API/pipeline instances.
ApiKeys__KeyRingPath optionally selects the deployment's protected persistent directory;
otherwise ASP.NET's host defaults apply. Configure host-level key-at-rest protection
and back up the ring. Losing the ring requires sellers to replace their saved keys.
Pipeline implementations can inject IApiKeyCredentials to decrypt an owner-scoped,
active, non-deleted credential on the server; no credential read endpoint is exposed.
Legacy ciphertext in an unknown format fails validation safely and must be replaced.

Database/timeout failures use MSG16 with user-safe text. The supplied specification
does not define MSG16's exact wording; this feature uses “Unable to load API keys.
Please refresh the page to try again.” Network failures show the same message.
The page explains masking; the connection form explains encryption and never reloads
the saved raw value into the browser.
Validation: dotnet test Capstone.sln runs the unit suite. For the opt-in PostgreSQL
test, set APCS_TEST_CONNECTION_STRING and run dotnet test
tests/APCS.Infrastructure.IntegrationTests/APCS.Infrastructure.IntegrationTests.csproj.
It uses a session-local temporary api_keys table with synthetic records for two
owners and always rolls back; public rows are untouched.
ApiKeyRepositoryTests also commits add/delete operations against a session-local
temporary table to exercise write SQL and the owner lock; public rows are untouched.

On Windows PowerShell, if Test-WithCoverage.ps1 rejects --results-directory because
the workspace path contains spaces, run the same Release tests with
`dotnet test Capstone.sln -c Release --no-build -- --coverage --coverage-output-format cobertura --report-trx`
and point ReportGenerator at `tests/**/bin/Release/net8.0/TestResults/*.cobertura.xml`
using the same class filters and thresholds from the script.
