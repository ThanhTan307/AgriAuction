module.exports = {
  content: [
    './pages/admin/category-management.html', './pages/login.html',
    './assets/js/category-management.js', './assets/js/main.js'
  ],
  darkMode: 'class',
  theme: {
    extend: {
      // Freeze the original Stitch palette rather than adopting new default colors.
      colors: {
        primary: '#15803d', 'primary-container': '#15803d', 'primary-fixed': '#95f8a7', secondary: '#0051d5',
        slate: { 50:'#f8fafc',100:'#f1f5f9',200:'#e2e8f0',300:'#cbd5e1',400:'#94a3b8',500:'#64748b',600:'#475569',700:'#334155',800:'#1e293b',900:'#0f172a',950:'#020617' },
        emerald: { 50:'#ecfdf5',100:'#d1fae5',200:'#a7f3d0',300:'#6ee7b7',400:'#34d399',500:'#10b981',600:'#059669',700:'#047857',800:'#065f46',900:'#064e3b',950:'#022c22' },
        amber: { 200:'#fde68a',800:'#92400e',900:'#78350f' },
        red: { 50:'#fef2f2',600:'#dc2626',700:'#b91c1c' }
      },
      fontFamily: { sans: ['Roboto', '-apple-system', 'sans-serif'], mono: ['Roboto', 'sans-serif'] },
      boxShadow: { xs: '0 1px 2px 0 rgb(0 0 0 / 0.05)' }
    }
  }
};
