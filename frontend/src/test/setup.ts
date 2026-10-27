import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'
afterEach(cleanup)

// jsdom does not implement native dialogs. This models visibility only, never browser focus/top-layer proof.
if (typeof HTMLDialogElement !== 'undefined' && !HTMLDialogElement.prototype.showModal) HTMLDialogElement.prototype.showModal = function () { this.open = true }
if (typeof HTMLDialogElement !== 'undefined' && !HTMLDialogElement.prototype.close) HTMLDialogElement.prototype.close = function () { this.open = false; this.dispatchEvent(new Event('close')) }
