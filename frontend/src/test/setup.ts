import '@testing-library/jest-dom/vitest'

/*
 * jsdom implements <dialog> but not showModal/close. The admin dialogs use the real element on
 * purpose — focus trapping, Escape and the backdrop come from the platform rather than from a
 * dependency — so the test environment gets the two missing methods rather than the component
 * getting a hand-rolled modal.
 */
if (typeof HTMLDialogElement !== 'undefined' && !HTMLDialogElement.prototype.showModal) {
  HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) {
    this.open = true
  }

  HTMLDialogElement.prototype.close = function close(this: HTMLDialogElement) {
    this.open = false
    this.dispatchEvent(new Event('close'))
  }
}
