(async function () {
  'use strict';
  const P=Phase3, $=id=>document.getElementById(id), notice=$('page-message');
  let page=1, query='', selected, registered, busy=false, serial=0, detailSerial=0;
  P.init();
  async function load() {
    const request=++serial; $('market-grid').setAttribute('aria-busy','true'); $('market-grid').replaceChildren(); $('market-state').textContent='Đang tải phiên…';
    for(const id of ['market-total','market-open','market-mine']) $(id).textContent='—';
    $('page-prev').disabled=$('page-next').disabled=true;
    try {
      const filters=new URLSearchParams();for(const key of ['categoryId','location','state']){const value=document.querySelector(`[data-market-filter="${key}"]`)?.value;if(value)filters.set(key,value);}
      const [data,mine]=await Promise.all([AgriApi.request(`/api/v1/auctions?page=${page}&pageSize=12&query=${encodeURIComponent(query)}&${filters}`),AgriApi.request('/api/v1/me/registrations?pageSize=1')]);
      if(request!==serial) return;
      $('market-total').textContent=data.total; $('market-open').textContent=data.items.filter(a=>a.registrationOpen).length; $('market-mine').textContent=mine.total; $('market-live').textContent='Chưa mở';
      for(const a of data.items) {
        const card=P.node('article',undefined,'p3-auction-card'); card.dataset.auctionId=a.id;
        if(a.images.length) { const image=P.node('img'); image.src=a.images[0].url; image.alt='Ảnh hồ sơ lô '+a.content.title; image.loading='lazy'; card.append(image); }
        const btnLabel=a.registrationOpen?'Xem hồ sơ & đăng ký':'Xem hồ sơ phiên';
        const content=P.node('div'); content.append(P.status(a.state),P.node('h2',a.content.title),P.node('p',a.categoryName+' · '+a.content.location),P.node('p',`${Phase2.decimal(a.content.weight)} ${a.content.weightUnit} · Nông dân: ${a.farmerName}`),P.node('p','Cọc: '+P.amount(a.terms.depositAmount,a.terms.currency),'p3-deposit-figure'),P.node('p','Hết hạn đăng ký: '+Phase2.time(a.terms.registrationClosesAt)),P.node('p',a.registrationOpen?'Đang nhận đăng ký mô phỏng':'Hiện không nhận đăng ký'),P.button(btnLabel,()=>detail(a.id),Boolean(a.registrationOpen))); card.append(content); $('market-grid').append(card);
      }
      $('market-state').textContent=data.items.length?'':'Chưa có phiên phù hợp được công bố mô phỏng.';
      P.pager(data,$('page-prev'),$('page-next'),$('page-number'));
    } catch(error) { if(request===serial) { $('market-state').textContent='Không tải được phiên. Dùng “Tải lại phiên” để thử lại.'; Phase2.error(notice,error); } }
    finally { if(request===serial) $('market-grid').setAttribute('aria-busy','false'); }
  }
  async function detail(id) {
    const request=++detailSerial;
    selected=null; registered=null; $('auction-register').disabled=true; $('auction-detail').replaceChildren(); $('auction-title').textContent='Đang tải hồ sơ…'; Phase2.message($('auction-message'),''); $('auction-dialog').showModal();
    try {
      const [a,mine]=await Promise.all([AgriApi.request('/api/v1/auctions/'+id),AgriApi.request(`/api/v1/auctions/${id}/my-registration`)]);
      if(request!==detailSerial||!$('auction-dialog').open) return; selected=a; registered=mine.registration;
      $('auction-title').textContent=a.content.title;
      const meta=P.node('p',`Nông dân: ${a.farmerName} `,'p3-detail-meta');meta.append(Phase2.reference(a.id));
      const fields=P.node('dl',undefined,'p2-attributes'); Phase2.attributes(fields,a.content,a.categoryName);
      const images=P.node('div',undefined,'p2-gallery'); Phase2.gallery(images,a.images);
      $('auction-detail').append(meta,P.terms(a),images,fields);
      $('auction-register').disabled=!!registered||!a.registrationOpen;
      $('auction-register').textContent=registered?'Đã đăng ký · '+P.labels[registered.state]:'Đăng ký mô phỏng';
      if(registered) Phase2.message($('auction-message'),'Đã có đăng ký. Xem trạng thái xác nhận trong trang Đăng ký & cọc mô phỏng.');
      else if(!a.registrationOpen) Phase2.message($('auction-message'),'Hiện không nhận đăng ký. Kiểm tra lịch đăng ký của phiên ở trên.');
    } catch(error) { if(request===detailSerial) { $('auction-title').textContent='Không tải được hồ sơ'; Phase2.error($('auction-message'),error); } }
  }
  $('auction-register').addEventListener('click',async()=>{
    if(!selected||registered||busy) return; busy=true; $('auction-register').disabled=true; Phase2.message($('auction-message'),'Đang gửi đăng ký…');
    try {
      registered=await AgriApi.request(`/api/v1/auctions/${selected.id}/registrations`,{method:'POST',body:{}});
      $('auction-register').textContent='Đã đăng ký · '+P.labels[registered.state]; Phase2.message($('auction-message'),'Đã lưu đăng ký mô phỏng. Chờ quản trị viên xác nhận cọc giả định.','success'); await load();
    } catch(error) { Phase2.error($('auction-message'),error); }
    finally { busy=false; $('auction-register').disabled=!!registered||!selected.registrationOpen; }
  });
  $('market-search-form').addEventListener('submit',event=>{event.preventDefault(); query=$('market-query').value.trim(); page=1; Phase2.message(notice,''); load();});
  $('market-reset').addEventListener('click',()=>{query=''; $('market-query').value='';document.querySelectorAll('[data-market-filter]').forEach(n=>n.value=''); page=1; load();});
  $('market-reload').addEventListener('click',()=>{Phase2.message(notice,''); load();});
  $('page-prev').addEventListener('click',()=>{page--;load();}); $('page-next').addEventListener('click',()=>{page++;load();});
  try { await Phase2.ready('Enterprise');
    try{if((await AgriApi.request('/api/v1/capabilities')).completion){const form=$('market-search-form');for(const [key,label,type]of[['categoryId','Loại nông sản','select'],['location','Địa phương (text)','input'],['state','Trạng thái phiên','select']]){const input=P.node(type),l=P.node('label',label);input.id='market-filter-'+key;input.dataset.marketFilter=key;l.htmlFor=input.id;if(type==='input')input.maxLength=160;else{const all=P.node('option','Tất cả');all.value='';input.append(all);const options=key==='categoryId'?(await AgriApi.request('/api/v1/catalog/categories?pageSize=100')).items.map(c=>[c.id,c.name]):[['Scheduled','Đã công bố'],['Prepared','Đã cấu hình'],['Open','Đang đấu giá'],['ClosedWithWinner','Đã chốt có người thắng'],['ClosedNoWinner','Đã chốt không người thắng']];for(const [value,text]of options){const o=P.node('option',text);o.value=value;input.append(o);}}form.insertBefore(l,form.querySelector('button'));form.insertBefore(input,form.querySelector('button'));}}}catch(error){if(error.status!==404)throw error;}
    await load(); } catch(error) { $('market-state').textContent='Không thể truy cập dữ liệu.'; $('market-grid').setAttribute('aria-busy','false'); Phase2.error(notice,error); }
})();
