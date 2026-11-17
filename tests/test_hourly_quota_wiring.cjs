const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const form = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.cs'), 'utf8');
const alerts = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.HourlyQuota.cs'), 'utf8');
const quota = fs.readFileSync(path.join(__dirname, '../AppSource/HourlyServerQuota.cs'), 'utf8');
const config = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationModels.cs'), 'utf8');

assert.match(config, /ServerHourlyCounters/);
assert.match(quota, /job\.SiteId/);
assert.match(quota, /job\.Server/);
assert.match(quota, /KoreaOffset = TimeSpan\.FromHours\(9\)/);
assert.match(quota, /(?:ID\|아이디)/);
assert.match(form, /HourlyServerQuota\.Current\(_config\.ServerHourlyCounters, job, now\)/);
assert.match(form, /HourlyServerQuota\.MarkAttempt/);
assert.match(form, /HourlyServerQuota\.NextAllowedUtc/);
assert.match(form, /ReconcileSiteQuota\(job, reportedRemaining\)/);
assert.match(form, /_siteReportedIdRemaining = HourlyServerQuota\.ParseIdRemaining\(message\)/);
assert.doesNotMatch(form, /_nextGlobalAllowedUtc/);
assert.doesNotMatch(form, /_config\.SubmissionAttemptsUtc\.Count >= hourlyLimit/);
assert.match(alerts, /PasswordVault\.Protect/);
assert.match(alerts, /TelegramQuotaAlerts\.SendAsync/);
assert.match(alerts, /HourlyServerQuota\.Reconcile/);
assert.doesNotMatch(alerts, /PauseScheduler/);
console.log('PASS server-scoped hourly dispatch, site ID-only reconciliation, encrypted Telegram notices');
