(function(){
  'use strict';

  // Polyfill replaceChildren for full cross-browser compatibility
  if (typeof Element !== 'undefined' && !Element.prototype.replaceChildren) {
    Element.prototype.replaceChildren = function (...nodes) {
      while (this.firstChild) this.removeChild(this.firstChild);
      this.append(...nodes);
    };
  }
  if (typeof DocumentFragment !== 'undefined' && !DocumentFragment.prototype.replaceChildren) {
    DocumentFragment.prototype.replaceChildren = function (...nodes) {
      while (this.firstChild) this.removeChild(this.firstChild);
      this.append(...nodes);
    };
  }

  const api=AgriApi;
  const el=(tag,text,className)=>{const n=document.createElement(tag);if(text!==undefined)n.textContent=text;if(className)n.className=className;return n;};
  function field(form,name,label,type='text',min=1,max=120){
    const wrap=el('div',undefined,'wide');
    const input=el('input'),l=el('label',label);
    input.id=name;input.name=name;input.type=type;input.required=true;input.minLength=min;input.maxLength=max;
    l.htmlFor=name;
    wrap.append(l,input);
    form.append(wrap);
    return input;
  }
  function submit(form,label){
    const wrap=el('div',undefined,'wide');
    wrap.style.marginTop='6px';
    const b=el('button',label);
    b.type='submit';
    b.className='p2-button';
    wrap.append(b);
    form.append(wrap);
    return b;
  }
  const states={Approved:'Đã duyệt',Rejected:'Bị từ chối',PendingReview:'Chờ duyệt'};
  const roles={Administrator:'Quản trị viên',Farmer:'Nông dân',Enterprise:'Doanh nghiệp'};

  async function init(){
    try{
      const capabilities=await api.request('/api/v1/capabilities');if(!capabilities.completion)return;
      if(document.querySelector('#login-form')){
        // Login page has dedicated register.html; keep helper minimal.
      }else if(document.querySelector('#profile-form')){
        const user=await api.request('/api/v1/me');
        const approvalText = states[user.approvalState]||Phase2.label(user.approvalState)||'Đang hoạt động';
        const profileCard = document.querySelector('#profile-card');
        if (profileCard && !document.querySelector('#profile-approval-status')) {
          const statusDiv = el('div', undefined, 'wide');
          statusDiv.id = 'profile-approval-status';
          const label = el('label', 'Trạng thái tài khoản');
          const input = el('input');
          input.value = approvalText;
          input.readOnly = true;
          statusDiv.append(label, input);
          const saveBtnWrap = document.querySelector('#profile-save')?.closest('.wide');
          if (saveBtnWrap) {
            document.querySelector('#profile-form')?.insertBefore(statusDiv, saveBtnWrap);
          } else {
            document.querySelector('#profile-form')?.append(statusDiv);
          }
        }
        const section=el('section',undefined,'p2-card'),form=el('form',undefined,'p2-form'),message=el('p');message.setAttribute('role','status');
        section.style.marginTop='20px';
        const h2=el('h2','Đổi mật khẩu tài khoản');
        const desc=el('p','Nhập mật khẩu hiện tại và mật khẩu mới để bảo mật tài khoản.','p2-muted');
        desc.style.marginBottom='16px';
        section.append(h2,desc,form,message);
        const old=field(form,'current-password','Mật khẩu hiện tại','password',1,1024),next=field(form,'new-password','Mật khẩu mới','password',8,1024);old.autocomplete='current-password';next.autocomplete='new-password';const button=submit(form,'Đổi mật khẩu và đăng nhập lại');
        form.addEventListener('submit',async event=>{event.preventDefault();if(!form.reportValidity())return;button.disabled=true;try{await api.request('/api/v1/me/change-password',{method:'POST',body:{currentPassword:old.value,newPassword:next.value}});old.value='';next.value='';api.resetCsrf();location.assign('/pages/login.html');}catch(error){message.textContent=error.message;button.disabled=false;}});(document.querySelector('.p2-profile')||document.querySelector('main')).append(section);
      }else if(document.querySelector('#account-list')){
        await AppNavigation.ready('Administrator');
        const tbody=document.querySelector('#account-tbody')||document.querySelector('#account-list'),message=document.querySelector('#page-message');
        const reviewDialog=document.querySelector('#account-review-dialog');
        const detailDialog=document.querySelector('#account-detail-dialog');
        let selectedAccount=null;

        // Wire up close buttons and backdrop clicks for dialogs
        document.querySelectorAll('#account-review-dialog [data-close-dialog], #account-detail-dialog [data-close-dialog]').forEach(btn => {
          btn.addEventListener('click', () => {
            btn.closest('dialog')?.close();
          });
        });
        [reviewDialog, detailDialog].forEach(dialog => {
          dialog?.addEventListener('click', (e) => {
            if (e.target === dialog) dialog.close();
          });
        });

        function openReview(a){
          selectedAccount=a;
          document.querySelector('#review-target').textContent=`${a.displayName} (${a.email}) · Vai trò: ${roles[a.role]||a.role}`;
          document.querySelector('#review-reason').value='';
          Phase2.message(document.querySelector('#review-message'),'');
          reviewDialog.showModal();
        }

        async function openDetail(a){
          selectedAccount=a;
          const info=document.querySelector('#detail-account-info');
          info.innerHTML=`
            <strong style="font-size:15px;color:#0f172a">${a.displayName}</strong>
            <p class="p2-muted" style="margin:4px 0 10px">${a.email} · Vai trò: ${roles[a.role]||Phase2.role(a.role)} · Trạng thái duyệt: <strong>${states[a.approvalState]||Phase2.label(a.approvalState)}</strong></p>
          `;
          document.querySelector('#sanction-status-text').textContent=a.biddingSuspendedUntil ? 'Đang tạm khóa tham gia đến: '+new Date(a.biddingSuspendedUntil).toLocaleString('vi-VN') : 'Quyền tham gia đấu giá: Bình thường';
          document.querySelector('#sanction-decision').value='';
          document.querySelector('#sanction-basis').value='';
          document.querySelector('#sanction-until').value='';
          Phase2.message(document.querySelector('#sanction-message'),'');

          const historyContainer=document.querySelector('#sanction-history-list');
          historyContainer.textContent='Đang tải nhật ký…';
          detailDialog.showModal();

          try{
            const [history,sanctions]=await Promise.all([
              api.request('/api/v1/admin/accounts/'+a.id+'/history'),
              api.request('/api/v1/admin/accounts/'+a.id+'/sanctions')
            ]);
            historyContainer.replaceChildren();
            const log=el('ol');log.style.paddingLeft='20px';log.style.lineHeight='1.7';
            for(const h of history)log.append(el('li',`${h.outcome==='Approve'?'Đã duyệt':'Đã từ chối'} · Lý do: ${h.reason} · ${new Date(h.occurredAt).toLocaleString('vi-VN')}`));
            for(const s of sanctions)log.append(el('li',`Chế tài: ${s.kind==='SuspendBidding'?'Tạm khóa tham gia':'Gỡ tạm khóa'} · ${s.reason} · ${new Date(s.occurredAt).toLocaleString('vi-VN')}`));
            if(!history.length&&!sanctions.length)log.append(el('li','Chưa có lịch sử thay đổi.','p2-muted'));
            historyContainer.append(log);
          }catch(err){
            historyContainer.textContent='Không tải được nhật ký.';
          }
        }

        // Setup review dialog action buttons
        document.querySelector('#btn-approve-account')?.addEventListener('click',async()=>{
          if(!selectedAccount)return;
          const reasonInput=document.querySelector('#review-reason');
          if(!reasonInput.reportValidity())return;
          const btn=document.querySelector('#btn-approve-account');btn.disabled=true;
          Phase2.message(document.querySelector('#review-message'),'Đang phê duyệt…');
          try{
            await api.request('/api/v1/admin/accounts/'+selectedAccount.id+'/review',{method:'POST',body:{expectedVersion:selectedAccount.approvalVersion,outcome:'Approve',reason:reasonInput.value}});
            reviewDialog.close();
            Phase2.message(message,'Đã phê duyệt tài khoản thành công.','success');
            await load();
          }catch(err){
            Phase2.message(document.querySelector('#review-message'),err.message,'error');
            btn.disabled=false;
          }
        });

        document.querySelector('#btn-reject-account')?.addEventListener('click',async()=>{
          if(!selectedAccount)return;
          const reasonInput=document.querySelector('#review-reason');
          if(!reasonInput.reportValidity())return;
          const btn=document.querySelector('#btn-reject-account');btn.disabled=true;
          Phase2.message(document.querySelector('#review-message'),'Đang từ chối…');
          try{
            await api.request('/api/v1/admin/accounts/'+selectedAccount.id+'/review',{method:'POST',body:{expectedVersion:selectedAccount.approvalVersion,outcome:'Reject',reason:reasonInput.value}});
            reviewDialog.close();
            Phase2.message(message,'Đã từ chối tài khoản.','success');
            await load();
          }catch(err){
            Phase2.message(document.querySelector('#review-message'),err.message,'error');
            btn.disabled=false;
          }
        });

        // Setup sanction dialog action buttons
        async function handleSanction(kind){
          if(!selectedAccount)return;
          const decision=document.querySelector('#sanction-decision');
          const basis=document.querySelector('#sanction-basis');
          const until=document.querySelector('#sanction-until');
          const sanctionMessage=document.querySelector('#sanction-message');
          if(!basis.reportValidity())return;
          if(kind==='SuspendBidding'&&!decision.value.trim()){Phase2.message(sanctionMessage,'Cần nhập mã quyết định khiếu nại để áp dụng chế tài.','error');return;}
          if(kind==='SuspendBidding'&&!until.value){Phase2.message(sanctionMessage,'Cần nhập mốc thời gian kết thúc tạm khóa.','error');return;}

          Phase2.message(sanctionMessage,'Đang xử lý chế tài…');
          try{
            const key=crypto.randomUUID();
            const payload={decisionEntryId:decision.value.trim()||null,expectedVersion:selectedAccount.restrictionVersion,kind,reason:basis.value.trim(),until:kind==='SuspendBidding'?new Date(until.value).toISOString():null};
            await api.request('/api/v1/admin/accounts/'+selectedAccount.id+'/sanctions',{method:'POST',headers:{'Idempotency-Key':key},body:payload});
            detailDialog.close();
            Phase2.message(message,'Đã cập nhật chế tài tài khoản.','success');
            await load();
          }catch(err){
            Phase2.message(sanctionMessage,err.message,'error');
          }
        }
        document.querySelector('#btn-apply-sanction')?.addEventListener('click',()=>handleSanction('SuspendBidding'));
        document.querySelector('#btn-lift-sanction')?.addEventListener('click',()=>handleSanction('LiftBiddingSuspension'));

        async function load(){
          try{
            tbody.replaceChildren();
            const rows=await api.request('/api/v1/admin/accounts');
            if(!rows.length){
              const tr=el('tr');const td=el('td','Chưa có tài khoản thành viên nào.','p2-muted');td.colSpan=5;td.style.padding='24px';td.style.textAlign='center';tr.append(td);tbody.append(tr);return;
            }
            for(const a of rows){
              const tr=el('tr');tr.dataset.accountId=a.id;
              // 1. Thành viên
              const tdUser=el('td');
              const nameStrong=el('strong',a.displayName);nameStrong.style.display='block';nameStrong.style.fontWeight='500';
              const emailSmall=el('small',a.email);emailSmall.style.color='#64748b';
              tdUser.append(nameStrong,emailSmall);
              tr.append(tdUser);

              // 2. Vai trò
              const tdRole=el('td',roles[a.role]||Phase2.role(a.role));
              tr.append(tdRole);

              // 3. Trạng thái duyệt
              const tdState=el('td');
              const stateBadge=el('span',states[a.approvalState]||Phase2.label(a.approvalState),'p2-state');
              stateBadge.dataset.state=a.approvalState;
              tdState.append(stateBadge);
              tr.append(tdState);

              // 4. Quyền đấu giá
              const tdSanction=el('td');
              if(a.biddingSuspendedUntil){
                const sus=el('span','Tạm khóa đến '+Phase2.time(a.biddingSuspendedUntil),'p2-state');
                sus.dataset.state='Rejected';
                tdSanction.append(sus);
              }else{
                const ok=el('span','Bình thường','text-xs text-slate-500');
                tdSanction.append(ok);
              }
              tr.append(tdSanction);

              // 5. Thao tác
              const tdAction=el('td');tdAction.className='p3-col-actions';
              const group=el('div',undefined,'p2-actions');
              group.style.margin='0';group.style.justifyContent='flex-end';group.style.gap='6px';

              // Only show review button when account is PendingReview
              if(a.approvalState==='PendingReview'){
                const reviewBtn=el('button','Xét duyệt','p2-button text-xs py-1 px-3');
                reviewBtn.type='button';
                reviewBtn.addEventListener('click',()=>openReview(a));
                group.append(reviewBtn);
              }

              const detailBtn=el('button','Chi tiết & Chế tài','p2-secondary text-xs py-1 px-3');
              detailBtn.type='button';
              detailBtn.addEventListener('click',()=>openDetail(a));
              group.append(detailBtn);

              tdAction.append(group);
              tr.append(tdAction);
              tbody.append(tr);
            }
          }catch(error){Phase2.error(message,error);}
        }
        document.querySelector('#accounts-reload').addEventListener('click',load);await load();
      }
    }catch(error){if(error.status===404)return;const target=document.querySelector('#page-message')||document.querySelector('#login-message');if(target)target.textContent=error.message;}
  }
  init();
})();
