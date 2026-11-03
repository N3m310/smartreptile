# Mobile end-to-end tests

These tests drive the Flutter app on an Android emulator and use the local .NET API. The 13 end-to-end scenarios
cover invalid login, language selection, registration and session handling, primary navigation, terrarium validation
and CRUD, history controls, alert filters, species search and details, and password validation. They are separate
from the unit and widget tests under `test/`.

## Run

1. Start the local API using the project's .NET workflow. From the repository root:

   ```powershell
   dotnet run --project backend/src/SmartReptile.Api
   ```

   Ensure the API's configured database and other local dependencies are available. In a separate terminal, wait
   for readiness:

   ```powershell
   curl.exe http://localhost:8080/health/ready
   ```

2. Start an Android emulator, then from `app` run:

   ```powershell
   flutter test integration_test --device-id emulator-5554 --reporter expanded
   ```

   The default API URL is `http://10.0.2.2:8080`, which reaches the host machine from the Android emulator. For a
   different API address, pass `--dart-define=API_BASE=http://<host>:<port>`.

The suite creates a unique Owner fixture through the API and reuses it across scenarios; the registration scenario
also verifies registration through the app UI. Terrarium operations are performed through the UI. Teardown deletes
any remaining terrarium with that exact generated name. Test accounts are identifiable by the `mobile_e2e_`
username prefix.

The Flutter test runner prints each scenario and the final pass/fail summary. The checked-in
[`TEST-RESULTS.md`](./TEST-RESULTS.md) records the most recent attempted run.
