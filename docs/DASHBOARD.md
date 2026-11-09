# Dashboards

## Client dashboard
Address: `https://<service address>/dashboard`. The client signs in with only its API key and sees its own requests: when each was made, the service, the status, the time taken, the tokens used, and the full result (formatted JSON or text). It also shows the token limit and how much is used, a chart of tokens per day, and filters for period, service and status. The key is kept in the browser tab's memory only.

A deactivated or expired key shows the reason instead of data. The old shared key has no history. On ngrok's free plan a browser first shows ngrok's warning page once.

## Admin activity
Address: `http://localhost:8090/admin/activity` on the server (or the **All requests** and **Requests** buttons of the keys page). Same page, for all clients or one chosen client, with a Client column. It works only from the server itself and asks for the admin password.

## What is stored
- Resume uploads, match scores and invoices are jobs: the jobs table holds the file name, status, time, result and error. Tokens come from the usage table.
- Direct calls (job description, goals, pasted-text resume parsing) are stored in the usage table with their time, a short description, the result and any error.
- Set `Activity:StoreResults` to `false` to stop keeping results of direct calls.
- Results contain customer data. Decide a retention period and clean up old rows. Deleting usage rows lowers the "total" token count of keys whose limit period is `Total`.