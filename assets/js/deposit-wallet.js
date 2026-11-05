(async function () {
  'use strict';
  const $=id=>document.getElementById(id); let page=1, serial=0; Phase3.init();
  async function load() {
    const request=++serial; $('registration-list').setAttribute('aria-busy','true');
    $('page-prev').disabled=$('page-next').disabled=true;
    try {
      const data=await AgriApi.request(`/api/v1/me/registrations?page=${page}&pageSize=20`); if(request!==serial) return;
      Phase3.registrationRows(data,false); $('wallet-total').textContent=data.total; $('wallet-pending').textContent=data.items.filter(r=>r.state==='PendingDeposit').length; $('wallet-confirmed').textContent=data.items.filter(r=>r.state==='SimulationEligible').length; $('wallet-real').textContent='Không có';
      Phase3.pager(data,$('page-prev'),$('page-next'),$('page-number'));
    } catch(error) { if(request===serial) { $('registration-state').textContent='Không tải được đăng ký. Hãy tải lại.'; Phase2.error($('page-message'),error); } }
    finally { if(request===serial) $('registration-list').setAttribute('aria-busy','false'); }
  }
  $('wallet-reload').addEventListener('click',()=>{Phase2.message($('page-message'),'');load();}); $('page-prev').addEventListener('click',()=>{page--;load();}); $('page-next').addEventListener('click',()=>{page++;load();});
  try {
    await Phase2.ready('Enterprise');
    await load();
    const refresh = () => { if (!document.hidden) load(); };
    const timer = setInterval(refresh, 10000);
    document.addEventListener('visibilitychange', refresh);
    window.addEventListener('focus', refresh);
    window.addEventListener('pagehide', () => { clearInterval(timer); }, { once: true });
  } catch(error) { $('registration-state').textContent='Không thể truy cập dữ liệu.'; $('registration-list').setAttribute('aria-busy','false'); Phase2.error($('page-message'),error); }
})();
