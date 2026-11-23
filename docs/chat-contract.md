# Chat Contract (HRMS <-> HrmsAi)

Status: DRAFT v0.6 (Step 1: greeting, basic chat, memory)

## 1. Overview
The HRMS server keeps ONE SignalR connection open to the HrmsAi chat hub.
All users share it. Every message carries the conversation id, so replies
can be sent to the right browser.

    Browser <-> HRMS ChatHub <-> (one shared connection) <-> HrmsAi ChatHub

The browser never connects to the AI server.

## 2. Connection
- URL: `https://<ai-server>/api/chat/hub`
- Header: `X-Api-Key: hrms_xxxxx` (the key must be allowed for module `chat`)
- Transport: WebSockets
- The connection must reconnect automatically.
- If the connection drops, running answers are lost. The HRMS should show
  "Something went wrong, please ask again".

## 3. Calls from HRMS to AI (hub methods)

### StartChat
Starts a new conversation, or continues an old one.

Request:
    {
      "externalUserId": "245",
      "userName": "Hamid",
      "language": "en",
      "conversationId": null
    }

- `externalUserId`: the HRMS user id (text). It comes from the HRMS login, never from the browser.
- `conversationId`: send it to continue an old chat. Send null for a new chat.

Returns:
    {
      "conversationId": "b2c1...-guid",
      "greeting": "Hi Hamid, how can I help you?",
      "isNew": true
    }

The greeting needs no AI, so it comes back at once.

### SendMessage
Sends one question.

Request:
    {
      "conversationId": "b2c1...-guid",
      "externalUserId": "245",
      "text": "How can I apply for leave?"
    }

Returns at once: `{ "accepted": true, "messageId": "guid" }`
or `{ "accepted": false, "errorCode": "BUSY" }`.
The answer then arrives as events (section 4).

Rules:
- Text is limited to 1000 characters (`TOO_LONG` if longer).
- Only one running answer per conversation (`BUSY` if another is running).
- `externalUserId` must match the user who started the conversation (`NOT_FOUND` if not).

## 4. Events from AI to HRMS (the HRMS must handle these)

| Event | Data | Meaning |
|---|---|---|
| `ReceiveToken` | conversationId, messageId, text | A piece of the answer. Append it. |
| `ReceiveDone` | conversationId, messageId, totalTokens | The answer is complete. |
| `ReceiveError` | conversationId, messageId, errorCode, message | The answer failed. |

## 5. Error codes
| Code | Meaning | What the user should see |
|---|---|---|
| `QUOTA_EXCEEDED` | The key's token limit is used up | "Chat is not available right now" |
| `BUSY` | Another answer is running in this conversation | "Please wait for the answer" |
| `TOO_LONG` | Message above 1000 characters | "Please shorten your message" |
| `NOT_FOUND` | Conversation not found for this user | Start a new chat |
| `AI_BUSY` | No free AI slot in time | "I'm busy, please try again" |
| `AI_FAILED` | The AI call failed | "Something went wrong, please try again" |

## 6. Memory
The AI server stores every message and gives the AI the last 6 messages
of the conversation. The HRMS does not need to send history.

## 7. Coming in later steps (not in v0.1)
- `GetHistory` (HRMS asks for old messages)
- `RunAction` (AI asks the HRMS for data such as leave balance)
- Knowledge search from uploaded documents

## 8. Changes in v0.2
- The hub address is `/api/chat/hub` (under the module prefix, so the key check sees module `chat`).
- Events carry ONE object each, for example `ReceiveToken` gets `{ conversationId, messageId, text }`.
- `SendMessage` returns the error codes `EMPTY`, `TOO_LONG`, `NOT_FOUND`, `QUOTA_EXCEEDED` and `BUSY` straight away
  (`accepted: false`). Only `AI_BUSY` and `AI_FAILED` arrive later as a `ReceiveError` event.
- `StartChat` with an unknown `conversationId` quietly starts a new conversation (`isNew: true`).
- `StartChat` throws a hub error if `externalUserId` is empty.

## 9. RunAction (AI asks the HRMS for data) - new in v0.3
The AI server calls the method `RunAction` on the HRMS connection and waits for the return value (time limit: 10 seconds).
The HRMS must register a handler named `RunAction` on its connection.

Request (one object):
    { "action": "get_employee_profile", "externalUserId": "245" }

Response (the HRMS returns one object):
    { "ok": true,  "data": { ...any JSON... } }
    { "ok": false, "error": "NOT_FOUND" }

Rules:
- `externalUserId` is the user of the conversation. The AI never takes it from the question text.
- The HRMS decides what each action returns and must check permissions itself.
- Return only what the user may see. Leave out secrets (password hashes, bank numbers, tax ids).
- The AI server keeps `get_employee_profile` in memory for 30 minutes per user.
- If the HRMS does not answer in time or returns `ok: false`, the user sees "I can't reach the HR system right now".

### Actions
| Action | Returns | Used when the user asks about |
|---|---|---|
| `get_employee_profile` | Employee record: name, department, sub department, designation, manager, joining date, branch and similar | own department, manager, designation, joining date, employee id |

More actions (leave balance and others) come in a later step.
## 10. RunAction: replaces section 9 (v0.4)
The first design (the HRMS handler returns a value) is dropped. Use a request event and a reply call.

AI to HRMS: event `RunAction` (one object):
    { "requestId": "guid", "action": "get_employee_profile", "externalUserId": "245" }

HRMS to AI: hub method `SubmitActionResult` (one object):
    { "requestId": "same guid", "ok": true,  "data": { ...any JSON... } }
    { "requestId": "same guid", "ok": false, "error": "NOT_FOUND" }

Rules:
- The HRMS must reply on the same connection that received the request.
- The AI server waits 10 seconds. After that the user sees "I can't reach the HR system right now".
- All other rules from section 9 stay (user id from the conversation, no secrets, permission checks in the HRMS, profile kept 30 minutes).
## 11. Action: get_leave_balance (v0.5)
Asked when the user wants their own leave balance ("how many leaves do I have", "sick leave balance").
The HRMS returns the balance for `externalUserId` only. The reply text is built by the AI server from this data.
Not cached (balances change when leave is approved).

    { "requestId": "guid", "ok": true,
      "data": { "balances": [
        { "leaveType": "Casual Leave", "total": 12, "used": 3, "remaining": 9 } ] } }

- `leaveType` and `remaining` are required for each item. `total` and `used` are optional.
- Numbers may have decimals (for example 9.5).
- Return an empty `balances` list when the user has no leave types. Return `ok: false` only on errors.
## 12. ReceiveCard event (v0.6)
Some answers are a short sentence (`ReceiveToken`) plus a card. The AI server sends `ReceiveCard` after the token and before `ReceiveDone`.

    { "conversationId": "guid", "messageId": "guid",
      "card": { "type": "table", "title": "Leave balance",
                "columns": ["Leave type","Total","Used","Remaining"],
                "rows": [["Casual Leave","12","3","9"]] } }

- Only `type: "table"` exists for now. A client that does not know a card type must ignore it (the text still makes sense).
- The HRMS passes the card to the browser as it is. Cell values are plain text and must never be shown as HTML.

## 13. get_attendance_summary

Attendance of the logged-in user for the current month, one row per day.
The AI server decides if the user wants today, this week or this month and filters the rows itself.
Never cached. The reply is written by code (intro sentence + table card), not by the AI.

Trigger questions: "my attendance today", "did I punch in today", "attendance this week", "my attendance this month".
Not triggered (knowledge base): "how do I regularize my attendance", "attendance policy".

HRMS returns (`SubmitActionResult` data):

```json
{
  "month": "2026-11",
  "today": "2026-11-23",
  "days": [
    { "date": "2026-11-02", "status": "Present",    "checkIn": "09:05", "checkOut": "18:10", "hours": 9.08 },
    { "date": "2026-11-09", "status": "Leave",      "checkIn": null,    "checkOut": null,    "hours": null },
    { "date": "2026-11-12", "status": "Absent",     "checkIn": null,    "checkOut": null,    "hours": null },
    { "date": "2026-11-16", "status": "Half Day",   "checkIn": "09:10", "checkOut": "13:30", "hours": 4.33 },
    { "date": "2026-11-21", "status": "Weekly Off", "checkIn": null,    "checkOut": null,    "hours": null },
    { "date": "2026-11-23", "status": "Present",    "checkIn": "09:05", "checkOut": null,    "hours": null }
  ]
}
```

Rules:
- `status` is one of: Present, Absent, Half Day, Leave, Holiday, Weekly Off.
- `date` is `yyyy-MM-dd`. Times are `HH:mm` in 24 hours, or null. `hours` is a number or null.
- Send rows from the 1st of the month up to today. Rows after `today` are ignored.
- The user is always `externalUserId`. Return null (ok:false or no data) when nothing is found.
- Today and this week: card `Date | Status | In | Out`. This month: card `Status | Days` (counts per status).
- At most 15 rows are shown. If there are more, the intro says "Showing 15 of N".
