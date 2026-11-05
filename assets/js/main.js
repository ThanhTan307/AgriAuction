/**
 * AgriAuction B2B Terminal - Common Core JS
 * Xử lý sự kiện chung: Sidebar toggle, Thu phóng (Zoom / Fit Screen), Tabs, Modals
 */

(function () {
  'use strict';

  // 1. Sidebar Toggle (Business & Admin)
  window.toggleSidebar = function () {
    const sidebar = document.getElementById('app-sidebar') || document.getElementById('admin-sidebar');
    const wrapper = document.getElementById('main-wrapper') || document.getElementById('admin-wrapper');
    const header = document.getElementById('admin-header');

    if (!sidebar || !wrapper) return;

    const isCollapsed = sidebar.classList.contains('-translate-x-full');

    if (isCollapsed) {
      sidebar.classList.remove('-translate-x-full');
      wrapper.classList.remove('pl-0');
      if (wrapper.id === 'admin-wrapper') {
        wrapper.classList.add('pl-[240px]');
        if (header) {
          header.classList.remove('left-0');
          header.classList.add('left-[240px]');
        }
      } else {
        wrapper.classList.add('pl-60');
      }
      localStorage.setItem('agri_sidebar_state', 'open');
    } else {
      sidebar.classList.add('-translate-x-full');
      wrapper.classList.remove('pl-60', 'pl-[240px]');
      wrapper.classList.add('pl-0');
      if (header) {
        header.classList.remove('left-[240px]');
        header.classList.add('left-0');
      }
      localStorage.setItem('agri_sidebar_state', 'collapsed');
    }
  };

  // 2. Zoom & Screen-Fit Controller
  window.setTerminalZoom = function (scale) {
    const target = document.getElementById('zoom-target') || 
                   document.getElementById('catalog-zoom-target') || 
                   document.getElementById('admin-zoom-target') ||
                   document.querySelector('main > div');
    if (!target) return;

    // Reset button states
    document.querySelectorAll('.zoom-btn, .cat-z-btn, .adm-z-btn').forEach(btn => {
      btn.classList.remove('bg-white', 'shadow-xs', 'text-primary', 'font-semibold', 'bg-primary-fixed', 'bg-primary/20');
      btn.classList.add('text-on-surface-variant', 'text-slate-600');
    });

    if (scale === 1) {
      target.style.zoom = '1';
      highlightZoomBtn(100);
    } else {
      target.style.zoom = scale;
      highlightZoomBtn(Math.round(scale * 100));
    }

    localStorage.setItem('agri_display_zoom', scale);
  };

  function highlightZoomBtn(pct) {
    const btn = document.getElementById('btn-zoom-' + pct) || 
                document.getElementById('cat-z-' + pct) || 
                document.getElementById('adm-z-' + pct);
    if (btn) {
      btn.classList.remove('text-on-surface-variant', 'text-slate-600');
      btn.classList.add('bg-white', 'shadow-xs', 'text-primary', 'font-semibold');
    }
  }

  window.fitTerminalScreen = function () {
    const target = document.getElementById('zoom-target') || 
                   document.getElementById('catalog-zoom-target') || 
                   document.getElementById('admin-zoom-target');
    if (!target) return;

    target.style.zoom = '1';
    const sidebar = document.getElementById('app-sidebar') || document.getElementById('admin-sidebar');
    const sidebarOpen = sidebar && !sidebar.classList.contains('-translate-x-full');

    const availableHeight = window.innerHeight - 60; // header space
    const availableWidth = window.innerWidth - (sidebarOpen ? 240 : 20) - 24;

    const contentHeight = target.scrollHeight;
    const contentWidth = target.scrollWidth;

    let scaleH = availableHeight / contentHeight;
    let scaleW = availableWidth / contentWidth;
    let fitScale = Math.min(scaleH, scaleW, 1);
    fitScale = Math.max(0.65, Math.round(fitScale * 100) / 100);

    target.style.zoom = fitScale;

    document.querySelectorAll('.zoom-btn, .cat-z-btn, .adm-z-btn').forEach(b => {
      b.classList.remove('bg-white', 'shadow-xs', 'text-primary', 'font-semibold', 'bg-primary-fixed');
      b.classList.add('text-on-surface-variant', 'text-slate-600');
    });

    const fitBtns = [
      document.getElementById('btn-zoom-fit'),
      document.getElementById('cat-z-fit'),
      document.getElementById('adm-z-fit')
    ];

    fitBtns.forEach(btn => {
      if (btn) {
        btn.classList.remove('text-on-surface-variant', 'text-slate-600');
        btn.classList.add('bg-primary-fixed', 'text-primary', 'font-bold');
        btn.textContent = 'Vừa (' + Math.round(fitScale * 100) + '%)';
      }
    });

    localStorage.setItem('agri_display_zoom', fitScale);
  };

  // 3. Tab switching helper
  window.switchTab = function (containerSelector, activeTabId) {
    const container = document.querySelector(containerSelector);
    if (!container) return;
    container.querySelectorAll('[data-tab-content]').forEach(content => {
      content.classList.toggle('hidden', content.id !== activeTabId);
    });
    container.querySelectorAll('[data-tab-btn]').forEach(btn => {
      const isActive = btn.getAttribute('data-tab-btn') === activeTabId;
      btn.classList.toggle('active', isActive);
    });
  };

  // Auto initialize layout on load
  window.addEventListener('DOMContentLoaded', function () {
    const isCollapsed = localStorage.getItem('agri_sidebar_state');
    if (isCollapsed === 'collapsed' || (!isCollapsed && window.innerWidth < 1366)) {
      window.toggleSidebar();
    }

    const savedZoom = localStorage.getItem('agri_display_zoom');
    if (savedZoom && savedZoom !== '1') {
      window.setTerminalZoom(parseFloat(savedZoom));
    } else if (window.innerHeight < 800) {
      window.fitTerminalScreen();
    }
  });

})();
