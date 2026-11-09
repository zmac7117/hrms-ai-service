# Services guide

All services use the same key: `X-API-Key: <key>` or `Authorization: Bearer <key>`. A key only works for the services ticked for it in the admin page (403 otherwise), and its token limit applies to all of them (429 when reached).

Direct calls (job description, goals) wait up to 30 seconds for a free AI slot and then answer **503** with a `Retry-After` header. Queued services (resume, match, invoice) return a job id.

## Job description generator (`jd`)
**Groq / OpenAI compatible route:** `POST /api/jd/v1/chat/completions`. It accepts the same body as Groq's chat completions (`model`, `messages`, `temperature`) and answers in the same shape (`choices[0].message.content`, `usage`). The model name sent by the caller is ignored. The service cleans the output: code fences and text around the HTML are removed.

To move an HRMS that calls Groq to this service, only the address changes. Make the URL configurable (for example `HrmsAi:JdChatUrl`), keep the Groq address as the default, and put the API key of this service in the existing key field. Allow a longer HTTP timeout (180 seconds).

**Native route:** `POST /api/jd/generate` with `{ "jobTitle": "...", "position", "company", "companyDescription", "department", "subDepartment", "grade", "gradeLevel", "location", "employeeType", "jobType", "yearsOfExperience", "certifications", "nationality" }` (only `jobTitle` is required). Returns `{ "html": "...", "model": "...", "tokens": 1234 }`.

Settings (`Jd`): `Temperature` (0.7), `MaxOutputTokens` (900), `MaxWaitSeconds` (30), `Model` (empty = default).

## Goals generator (`goals`)
**Groq / OpenAI compatible route:** `POST /api/goals/v1/chat/completions`, same idea as above. The content is the JSON the AI wrote, cleaned of code fences and surrounding text. If a screen expects an object such as `{ "goals": [ ... ] }` instead of a plain array, set `Goals:WrapArrayAsObject` to `true` (`Goals:ObjectKey` is the property name).

**Native route:** `POST /api/goals/generate` with `{ "summaryDescription": "...", "position", "department", "subDepartment", "employeeType", "grade", "gradeLevel", "goalCount" }` (`summaryDescription` required). Returns `{ "goals": [ { "goal": "...", "kpis": ["..."] } ], "model", "tokens" }`, or 422 when the AI did not return usable goals (just retry).

Settings (`Goals`): the same four as `Jd`, plus `WrapArrayAsObject` (false) and `ObjectKey` ("goals").

## Resume-to-job match score (`match`)
Speaks the same protocol as the SharpAPI service. In the HRMS set `SharpAPI:BaseUrl` to `https://<this service>/api/match` and `SharpAPI:ApiKey` to a key with the `match` module. No code change.

1. `POST {BaseUrl}/api/v1/hr/resume_job_match_score`, multipart form with `file` (PDF, DOCX or TXT), `content` (the job description as plain text) and `language`. Returns 202 with `job_id` and `status_url`.
2. `GET status_url` until `data.attributes.status` is `success` or `failed` (`pending`, `processing` in between). On success `data.attributes.result` is a **string** holding this JSON:
```json
{ "match_scores": { "overall_match": 72, "skills_match": 80, "experience_match": 70, "education_match": 60,
    "technical_stack_match": 75, "soft_skills_match": 50, "cultural_fit_potential": 50, "stability_score": 65,
    "language_proficiency_match": 50, "location_preference_match": 50 },
  "explanations": { "overall_match": "...", "skills_match": "...", "...": "..." },
  "summary": "...", "matched_skills": ["C#"], "missing_skills": ["Azure"], "meta": { "model": "...", "scored_at_utc": "..." } }
```
How the numbers are made: one AI call scores the dimensions and lists the skills the job requires. `skills_match` is calculated in code from the skills actually found in the resume text, and `overall_match` is a weighted average (skills 25%, experience 20%, technical stack 20%, education 10%, the other five 5% each). All text is HTML-encoded (`Match:EncodeHtml`), because HRMS screens usually insert it as HTML. Old `.doc` files and scans are not supported (422).

Settings (`Match`): `Temperature` (0), `MaxOutputTokens` (900), `MaxResumeChars` (12000), `MaxJobChars` (6000), `Model`, `EncodeHtml` (true), `PublicBaseUrl` (only behind a proxy that hides the public address).

## Invoice parsing (`invoice`)
For the Expense module. Accepts a PDF (with text or scanned) or an image (PNG, JPG, TIFF, BMP).

1. `POST /api/invoice/parse-file` (multipart, field `file`; optional field `lineItems=false` for totals only, which is faster). Returns 202 and a `jobId`.
2. Poll `GET /api/jobs/{jobId}` (the result is in `result`).
3. `GET /api/invoice/ocr-check` shows whether Tesseract and Poppler are installed.

Result (schema 1.0): `document` (`type`, `source` = `pdf-text` or `ocr`, `pages`), `vendor` (`name`, `taxId`, `address`), `invoice` (`number`, `date`, `dateText`, `dueDate`, `dueDateText`, `purchaseOrder`), `currency`, `expenseCategory`, `totals` (`subtotal`, `tax`, `discount`, `shipping`, `total`), `lineItems` (`description`, `quantity`, `unitPrice`, `amount`), `checks` (`lineItemsMatchSubtotal`, `totalsAddUp`), `possibleDuplicate` (`requestId`, `at`), `warnings`, `meta`. Unknown single values are `null`.

The AI copies amounts as printed. Code converts them and checks them: totals must add up, the total must appear in the text, dates become `yyyy-MM-dd` (an ambiguous `03/04/2026` is read according to `Invoice:DateOrder` and flagged), and the same invoice number and total sent twice by one client is flagged as a possible duplicate.

Recommended flow: attach the file to the claim, send it, poll, pre-fill an **editable** form, show `warnings`, highlight `possibleDuplicate`, and keep the original file in the HRMS (this service deletes its copy after reading unless `Invoice:KeepFiles` is true).

Settings (`Invoice`): `MaxFileSizeMb` (10), `StoragePath`, `KeepFiles` (false), `DateOrder` ("DMY"), `DefaultCurrency`, `IncludeLineItems` (true), `MaxLineItems` (30), `Categories` (comma separated), `Temperature`, `MaxOutputTokens` (1800), `MaxTextChars` (12000), `Model`. OCR settings (`Ocr`): `Language` (`eng`, or for example `eng+hin`), `PageSegMode` (6), `Dpi` (200), `MaxPages` (3), `TimeoutSeconds` (90), `ToolsRoot`, `TempPath`.

Limits: OCR plus a small model is good for clean invoices and receipts, weaker for crumpled or faded receipts, handwriting and dense tables. Always show the result for review.