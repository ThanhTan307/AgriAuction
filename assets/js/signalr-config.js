/**
 * AgriAuction B2B - SignalR / WebSocket Real-time Configuration Module
 * Cấu hình sẵn sàng kết nối Real-time Hub cho Sàn Đấu Giá Nông Sản
 */

(function (window) {
  'use strict';

  const SignalRConfig = {
    // 1. Endpoints
    hubUrl: window.AGRI_HUB_URL || '/hubs/auctionHub',
    fallbackPollingUrl: '/api/v1/auctions/poll',

    // 2. Connection Settings
    options: {
      transport: 1, // 1: WebSockets, 2: ServerSentEvents, 4: LongPolling
      reconnectIntervals: [0, 2000, 5000, 10000, 30000], // Exponential backoff (ms)
      pingInterval: 15000,
      timeout: 30000
    },

    // 3. Current Connection State
    state: {
      connected: true,
      latencyMs: 24,
      connectionId: 'SIG-B2B-' + Math.random().toString(36).substring(2, 9).toUpperCase(),
      activeAuctionId: 'AUC-2026-089'
    },

    // 4. Listeners Repository
    handlers: {
      onNewBid: [],
      onSoftClose: [],
      onAuctionEnd: [],
      onDepositChange: [],
      onConnectionStatusChange: []
    },

    // 5. Subscription API
    on: function (event, callback) {
      if (this.handlers[event]) {
        this.handlers[event].push(callback);
      }
    },

    emit: function (event, data) {
      if (this.handlers[event]) {
        this.handlers[event].forEach(fn => fn(data));
      }
    },

    // 6. Send Bid Command
    submitBid: function (bidData) {
      return new Promise((resolve, reject) => {
        // Giả lập thời gian round-trip tới server 45ms
        setTimeout(() => {
          if (bidData.amount && bidData.amount > 0) {
            resolve({
              success: true,
              transactionId: 'TX-' + Date.now(),
              fencingToken: 'v4-' + Math.random().toString(36).substring(2, 7),
              timestamp: new Date().toISOString()
            });
          } else {
            reject(new Error('Giá đặt không hợp lệ'));
          }
        }, 120);
      });
    },

    // 7. UI Network Status Indicator
    updateUiStatus: function (statusText, isOk, latency) {
      const indicators = document.querySelectorAll('.signalr-status-indicator');
      indicators.forEach(el => {
        el.innerHTML = `SignalR: <strong>${statusText}</strong> <span class="text-primary font-semibold">(${latency || this.state.latencyMs}ms)</span>`;
      });
    }
  };

  // Export to window
  window.SignalRConfig = SignalRConfig;

})(window);
