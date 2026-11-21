'use strict';

// Offline safety regression for editing a scheduled price again before it is
// submitted. No site requests or user settings are accessed by this test.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const source = name => fs.readFileSync(path.join(__dirname, '../AppSource', name), 'utf8');

const integrated = source('AutomationForm.IntegratedEditing.cs');
const site = source('AutomationForm.SitePriceEdit.cs');
const scheduler = source('AutomationForm.cs');
const main = source('MainForm.cs');
const panel = source('MainForm.IntegratedEditor.cs');
const updater = source('AppUpdateService.cs');

// A site-price request for ONE listing doesn't block other pending edits.
assert.match(integrated, /command\.Field == IntegratedEditField\.Price &&\s*_activeSitePriceJob is not null/);
assert.match(integrated, /_busy && _currentlyExecutingJob is null && !editingPendingPrices/);
assert.match(integrated, /_myListingsImportRunning \|\| _sitePriceBatchRunning/);
assert.match(integrated, /ReferenceEquals\(job, _activeSitePriceJob\)/);
assert.match(integrated, /Action afterSubmission = \(\) => ApplyBatchUndoChange\(change\)/);
assert.match(integrated, /_deferredExecutingJobEdits\.Add\(afterSubmission\)/);
assert.match(integrated, /_config\.Save\(\)/);

// Active target is not mutated while its request/readback is in progress.
assert.match(site, /_activeSitePriceJob = job;/);
assert.match(site, /foreach \(var deferredEdit in _deferredExecutingJobEdits\) deferredEdit\(\);/);
assert.match(site, /_deferredExecutingJobEdits\.Clear\(\);/);
assert.match(site, /_config\.Save\(\);[\s\S]*?_activeSitePriceJob = null;/);

// An old successful request cannot erase a freshly queued replacement.
assert.match(scheduler, /string submittedTarget = job\.PendingSitePriceTarget;/);
assert.match(scheduler, /if \(string\.Equals\(job\.PendingSitePriceTarget, submittedTarget, StringComparison\.Ordinal\)\)\s*job\.PendingSitePriceTarget = "";/);
assert.match(scheduler, /reserved != local/);
assert.match(panel, /기존 예약은 새 금액으로 교체합니다/);

// Immediate stop cannot terminate an unverified submission; updates wait for
// all systems to be idle and save the pending jobs outside the install root.
assert.match(scheduler, /if \(!_submissionVerificationPending\) _currentRun\?\.Cancel\(\)/);
assert.match(scheduler, /!_busy && !_schedulerRunning && !_myListingsImportRunning && !_pendingMyListingsImport/);
assert.match(main, /if \(!CanInstallUpdateAcrossSystems\)/);
assert.match(main, /_moduleA\.SaveListings\(\); _moduleB\.SaveListings\(\); _moduleC\.SaveListings\(\)/);
assert.match(updater, /public const string CurrentVersion = "22\.3\.20"/);
console.log('PASS re-edit queued prices, defer active submission edits, preserve latest request across stop/update');
