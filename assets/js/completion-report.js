(async function(){
  'use strict';
  try{
    if(!(await AgriApi.request('/api/v1/capabilities')).completion)return;
    const user=await AppNavigation.ready();if(!['Administrator','Farmer'].includes(user.role))return;
    
    const section=document.createElement('section');
    section.className='p2-card';
    section.innerHTML=`
      <div style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:8px">
        <h2 style="margin:0">Báo cáo giao dịch hoàn tất</h2>
        <span class="p7-meta">Đối soát tỷ lệ hoàn tất & doanh thu mô phỏng</span>
      </div>
      <form class="p7-completion-form">
        <label>Từ ngày
          <input id="completion-from" type="date" required>
        </label>
        <label>Đến hết ngày
          <input id="completion-through" type="date" required>
        </label>
        <button class="p2-button" type="submit">Xem báo cáo</button>
      </form>
      <p data-report-status role="status" aria-live="polite"></p>
      <div data-report-values></div>
      <p class="p2-muted" style="margin-top:14px;font-size:12px;line-height:1.5">
        Doanh thu mô phỏng tính theo ngày hoàn tất giao dịch. Tỷ lệ hoàn tất đối chiếu giữa số hồ sơ đã hoàn tất với nhóm phiên có người thắng chốt trong kỳ.
      </p>
    `;
    document.querySelector('main').append(section);
    
    const from=section.querySelector('#completion-from'),through=section.querySelector('#completion-through');
    const today=new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Ho_Chi_Minh'}).format(new Date());
    from.value=today.slice(0,8)+'01';
    through.value=today;

    async function load(){
      const status=section.querySelector('[data-report-status]'),values=section.querySelector('[data-report-values]');
      status.textContent='Đang tải…';
      try{
        const r=await AgriApi.request('/api/v1/me/reports/completion?from='+from.value+'&through='+through.value);
        if (typeof values.replaceChildren === 'function') {
          values.replaceChildren();
        } else {
          while (values.firstChild) values.removeChild(values.firstChild);
        }
        const formattedRevenue = Phase2.decimal(r.simulatedRevenue) + ' ' + (r.currency || 'VND');
        const successRatePercent = r.successRatePercent === null ? 'Chưa có' : `${r.successRatePercent}%`;
        const successCohortNote = r.successRatePercent === null ? 'Chưa có phiên trúng giá' : `${r.completedWinningCohort}/${r.winningAuctions} phiên trúng giá`;

        const grid = document.createElement('div');
        grid.className = 'p7-completion-grid';
        grid.innerHTML = `
          <article class="p7-accent">
            <h3>Doanh thu mô phỏng</h3>
            <strong>${formattedRevenue}</strong>
            <p>Giao dịch hoàn tất trong kỳ</p>
          </article>
          <article>
            <h3>Giao dịch hoàn tất</h3>
            <strong>${r.completedInPeriod}</strong>
            <p>Hồ sơ hoàn tất trong kỳ</p>
          </article>
          <article>
            <h3>Phiên có người thắng</h3>
            <strong>${r.winningAuctions}</strong>
            <p>Phiên chốt trúng trong kỳ</p>
          </article>
          <article>
            <h3>Đã hoàn tất nhóm phiên</h3>
            <strong>${r.completedWinningCohort}</strong>
            <p>Hồ sơ khớp nhóm phiên</p>
          </article>
          <article>
            <h3>Tỷ lệ hoàn tất giao dịch</h3>
            <strong>${successRatePercent}</strong>
            <p>${successCohortNote}</p>
          </article>
        `;

        values.append(grid);
        status.textContent='';
      }catch(error){
        status.textContent=error.message;
      }
    }
    section.querySelector('form').addEventListener('submit',event=>{event.preventDefault();load();});
    await load();
  }catch(error){
    if(error.status!==404)console.warn('Không tải được báo cáo mô phỏng.');
  }
})();
