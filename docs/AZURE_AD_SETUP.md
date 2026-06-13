# Azure AD App Registration Setup

You need an Azure AD (Entra ID) **App Registration** before VerseKit can connect to any Dynamics 365 / Power Platform environment. This is a one-time setup per environment.

---

## Step 1 — Create the App Registration

1. Go to [portal.azure.com](https://portal.azure.com) and sign in with an account that has Azure AD admin rights (or ask your tenant admin).
2. Navigate to **Azure Active Directory → App registrations → New registration**.
3. Fill in:
   - **Name**: `VerseKit` (any name)
   - **Supported account types**: *Accounts in this organizational directory only* (single tenant) — or *Multitenant* if you work across tenants
   - **Redirect URI**: Choose **Public client / native (mobile & desktop)** → enter `http://localhost`
4. Click **Register**.
5. Copy the **Application (client) ID** and **Directory (tenant) ID** from the Overview page — you will paste these into the app's connection form.

---

## Step 2 — Grant Dataverse Permissions

1. In your App Registration, go to **API permissions → Add a permission**.
2. Select **APIs my organization uses** and search for **Dataverse** (or **Common Data Service**).
3. Choose **Delegated permissions** → check `user_impersonation`.
4. Click **Add permissions**.
5. Click **Grant admin consent for [your tenant]** (requires admin).

> **Without admin consent** the first user to connect will see a consent prompt in the browser. That is fine for personal use — just click *Accept*.

---

## Step 3 — Configure the Connection in the App

Launch the app, click **Connect** in the toolbar, and fill in:

| Field | Value |
|---|---|
| Profile name | Any friendly name, e.g. `Contoso Dev` |
| Environment URL | `https://yourorg.crm.dynamics.com` (the URL from Power Platform Admin Center) |
| Client ID | The **Application (client) ID** from Step 1 |
| Tenant ID | The **Directory (tenant) ID** from Step 1 (leave blank to use the `/common` endpoint) |
| Auth method | **Interactive** (recommended for first-time setup) |

Click **Connect**. A browser window will open for Microsoft login. Sign in with your Dynamics 365 user account. After login the browser closes and the status bar shows *Connected: [profile name]*.

---

## Auth Method Reference

| Method | When to use | Setup |
|---|---|---|
| **Interactive** | Human users, dev machines | Browser popup, no extra config |
| **ClientSecret** | Automation, service accounts | Add a client secret under *Certificates & secrets* in the App Registration and paste it in the app |
| **DeviceCode** | Headless machines, no browser | Code printed in the app; you enter it at `microsoft.com/devicelogin` on any device |

---

## Finding Your Environment URL

- In **Power Platform Admin Center** ([admin.powerplatform.microsoft.com](https://admin.powerplatform.microsoft.com)), select your environment → **Settings → Environment URL**.
- Or: open Dynamics 365 in your browser — the URL bar shows `https://yourorg.crm4.dynamics.com` (the number after `crm` varies by region).

---

## Troubleshooting

| Error | Fix |
|---|---|
| `AADSTS50011: The reply URL does not match` | Ensure Redirect URI in the App Registration is exactly `http://localhost` (Public client type) |
| `AADSTS65001: No consent` | Click *Grant admin consent* in API permissions, or accept the consent prompt in the browser |
| `ServiceClient not ready` | Check the Environment URL has no trailing slash and starts with `https://` |
| `Unauthorized` | Ensure the user has a Dynamics 365 license assigned in the tenant |
