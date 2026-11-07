const { createApp, ref, computed, onMounted, watch, reactive } = Vue;

const API = window.location.origin + '/api';

async function apiFetch(url, opts = {}) {
  const res = await fetch(API + url, {
    headers: { 'Content-Type': 'application/json', ...(opts.headers || {}) },
    ...opts
  });
  if (res.status === 401) throw new Error('not connected');
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.error || res.statusText);
  }
  return res.json();
}

const app = createApp({
  setup() {
    const connected = ref(false);
    const vcInfo = ref('');
    const activeTab = ref('dashboard');
    const collapsed = ref(false);

    const stats = ref({});
    const vms = ref([]);
    const hosts = ref([]);
    const clusters = ref([]);
    const datastores = ref([]);

    const vmFilter = ref('');
    const selectedVMs = ref([]);
    const vmDetailVisible = ref(false);
    const vmDetail = ref(null);
    const vmTab = ref('power');
    const vmResources = ref(null);
    const vmSnapshots = ref([]);
    const cpuValue = ref(0);
    const memValue = ref(0);
    const newDiskGB = ref(10);
    const newSnapDialog = ref(false);
    const snapForm = reactive({ name: '', desc: '' });
    const batchSnapDialog = ref(false);
    const batchSnapForm = reactive({ name: 'Web_Batch', desc: '' });

    const hostDetailVisible = ref(false);
    const hostDetail = ref(null);
    const hostVMs = ref([]);

    const pageTitle = computed(() => ({
      dashboard: '仪表盘概览', vm: '虚拟机管理', host: '主机管理',
      cluster: '集群管理', datastore: '存储管理', session: '连接管理'
    }[activeTab.value] || ''));

    async function loadSession() {
      try {
        const s = await apiFetch('/session/status');
        connected.value = !!s.connected;
        vcInfo.value = s.vc || '';
      } catch { connected.value = false; }
    }

    async function loadDashboard() {
      if (!connected.value) return;
      try { stats.value = await apiFetch('/dashboard/stats'); } catch {}
    }

    async function loadVMs() {
      if (!connected.value) return;
      try { vms.value = await apiFetch('/vm' + (vmFilter.value ? ('?name=' + encodeURIComponent(vmFilter.value)) : '')); } catch {}
    }

    async function loadHosts() {
      if (!connected.value) return;
      try { hosts.value = await apiFetch('/host'); } catch {}
    }

    async function loadClusters() {
      if (!connected.value) return;
      try { clusters.value = await apiFetch('/cluster'); } catch {}
    }

    async function loadDatastores() {
      if (!connected.value) return;
      try { datastores.value = await apiFetch('/datastore'); } catch {}
    }

    function refresh() {
      loadSession();
      loadDashboard();
      if (activeTab.value === 'vm') loadVMs();
      if (activeTab.value === 'host') loadHosts();
      if (activeTab.value === 'cluster') loadClusters();
      if (activeTab.value === 'datastore') loadDatastores();
      ElementPlus.ElMessage.success('已刷新');
    }

    function onMenuSelect(idx) {
      activeTab.value = idx;
      refresh();
    }

    function onVmSelection(sel) { selectedVMs.value = sel; }

    async function vmPower(vm, action) {
      try {
        const resp = await apiFetch(`/vm/${vm.MoRef}/power`, {
          method: 'POST', body: JSON.stringify({ action })
        });
        if (resp.success) {
          ElementPlus.ElMessage.success('操作成功');
          loadVMs();
          if (vmDetail.value && vmDetail.value.MoRef === vm.MoRef) openVmDetail(vmDetail.value);
        } else {
          ElementPlus.ElMessage.error('操作失败');
        }
      } catch (e) { ElementPlus.ElMessage.error(e.message); }
    }

    async function openVmDetail(row) {
      vmDetail.value = row;
      vmDetailVisible.value = true;
      vmTab.value = 'power';
      try {
        vmDetail.value = await apiFetch(`/vm/${row.MoRef}/detail`);
        vmResources.value = await apiFetch(`/vm/${row.MoRef}/resources`);
        cpuValue.value = vmResources.value.NumCpu;
        memValue.value = vmResources.value.MemoryMB;
        vmSnapshots.value = await apiFetch(`/vm/${row.MoRef}/snapshots`);
      } catch (e) { ElementPlus.ElMessage.error(e.message); }
    }

    async function setCpu() {
      try {
        const r = await apiFetch(`/vm/${vmDetail.value.MoRef}/cpu`, {
          method: 'PUT', body: JSON.stringify({ count: cpuValue.value })
        });
        if (r.success) ElementPlus.ElMessage.success('CPU 已更新'); else ElementPlus.ElMessage.error('失败');
      } catch (e) { ElementPlus.ElMessage.error(e.message); }
    }

    async function setMemory() {
      try {
        const r = await apiFetch(`/vm/${vmDetail.value.MoRef}/memory`, {
          method: 'PUT', body: JSON.stringify({ memoryMB: memValue.value })
        });
        if (r.success) ElementPlus.ElMessage.success('内存已更新'); else ElementPlus.ElMessage.error('失败');
      } catch (e) { ElementPlus.ElMessage.error(e.message); }
    }

    async function addDisk() {
      try {
        const r = await apiFetch(`/vm/${vmDetail.value.MoRef}/disk`, {
          method: 'POST', body: JSON.stringify({ capacityGB: newDiskGB.value })
        });
        if (r.success) { ElementPlus.ElMessage.success('磁盘已添加'); openVmDetail(vmDetail.value); }
        else ElementPlus.ElMessage.error('失败');
      } catch (e) { ElementPlus.ElMessage.error(e.message); }
    }

    async function createSnap() {
      if (!snapForm.name) { ElementPlus.ElMessage.warning('请输入名称'); return; }
      try {
        const r = await apiFetch(`/vm/${vmDetail.value.MoRef}/snapshots`, {
          method: 'POST', body: JSON.stringify({ name: snapForm.name, description: snapForm.desc })
        });
        if (r.success) { ElementPlus.ElMessage.success('快照已创建'); newSnapDialog.value = false; vmSnapshots.value = await apiFetch(`/vm/${vmDetail.value.MoRef}/snapshots`); }
      } catch (e) { ElementPlus.ElMessage.error(e.message); }
    }

    async function revertSnap(row) {
      try {
        await ElementPlus.ElMessageBox.confirm(`确定回滚到快照 "${row.Name}"？`, '确认', { type: 'warning' });
        const r = await apiFetch(`/vm/${vmDetail.value.MoRef}/snapshots/${encodeURIComponent(row.Name)}/revert`, { method: 'POST' });
        if (r.success) ElementPlus.ElMessage.success('已回滚');
      } catch (e) { if (e !== 'cancel') ElementPlus.ElMessage.error(e.message); }
    }

    async function deleteSnap(row) {
      try {
        await ElementPlus.ElMessageBox.confirm(`确定删除快照 "${row.Name}"？`, '确认', { type: 'warning' });
        const r = await apiFetch(`/vm/${vmDetail.value.MoRef}/snapshots/${encodeURIComponent(row.Name)}`, { method: 'DELETE' });
        if (r.success) { ElementPlus.ElMessage.success('已删除'); vmSnapshots.value = await apiFetch(`/vm/${vmDetail.value.MoRef}/snapshots`); }
      } catch (e) { if (e !== 'cancel') ElementPlus.ElMessage.error(e.message); }
    }

    async function batchPower(action) {
      if (!selectedVMs.value.length) return;
      const actionMap = { on: '开机', off: '关机', offhard: '硬关机', restart: '重启', suspend: '挂起', reset: '强制重启' };
      try {
        await ElementPlus.ElMessageBox.confirm(`确定对选中的 ${selectedVMs.value.length} 台 VM 执行批量${actionMap[action]}？`, '批量操作', { type: 'warning' });
        const ids = selectedVMs.value.map(v => v.MoRef);
        const r = await apiFetch('/batch/power', { method: 'POST', body: JSON.stringify({ ids, action }) });
        ElementPlus.ElMessage.success(`完成！成功 ${r.success} / 失败 ${r.fail}`);
        loadVMs();
      } catch (e) { if (e !== 'cancel') ElementPlus.ElMessage.error(e.message); }
    }

    function batchSnap() { batchSnapDialog.value = true; }

    async function doBatchSnap() {
      try {
        const ids = selectedVMs.value.map(v => v.MoRef);
        const r = await apiFetch('/batch/snapshot', {
          method: 'POST', body: JSON.stringify({ ids, action: 'create', name: batchSnapForm.name, description: batchSnapForm.desc })
        });
        ElementPlus.ElMessage.success(`完成！成功 ${r.success} / 失败 ${r.fail}`);
        batchSnapDialog.value = false;
        loadVMs();
      } catch (e) { ElementPlus.ElMessage.error(e.message); }
    }

    async function openHostDetail(row) {
      hostDetail.value = row;
      hostDetailVisible.value = true;
      try {
        hostDetail.value = await apiFetch(`/host/${row.MoRef}/detail`);
        hostVMs.value = await apiFetch(`/host/${row.MoRef}/vms`);
      } catch (e) { ElementPlus.ElMessage.error(e.message); }
    }

    async function disconnect() {
      try {
        await ElementPlus.ElMessageBox.confirm('确定断开 vCenter 连接？', '确认', { type: 'warning' });
        await apiFetch('/session/disconnect', { method: 'POST' });
        connected.value = false;
        ElementPlus.ElMessage.success('已断开');
      } catch (e) { if (e !== 'cancel') ElementPlus.ElMessage.error(e.message); }
    }

    onMounted(async () => {
      await loadSession();
      if (connected.value) {
        await loadDashboard();
        if (activeTab.value === 'vm') await loadVMs();
        if (activeTab.value === 'host') await loadHosts();
        if (activeTab.value === 'cluster') await loadClusters();
        if (activeTab.value === 'datastore') await loadDatastores();
      }
      setInterval(loadSession, 15000);
    });

    watch(activeTab, async (tab) => {
      if (!connected.value) return;
      if (tab === 'vm') await loadVMs();
      if (tab === 'host') await loadHosts();
      if (tab === 'cluster') await loadClusters();
      if (tab === 'datastore') await loadDatastores();
      if (tab === 'dashboard') await loadDashboard();
    });

    return {
      connected, vcInfo, activeTab, collapsed, pageTitle,
      stats, vms, hosts, clusters, datastores,
      vmFilter, selectedVMs, vmDetailVisible, vmDetail, vmTab, vmResources, vmSnapshots,
      cpuValue, memValue, newDiskGB, newSnapDialog, snapForm,
      batchSnapDialog, batchSnapForm,
      hostDetailVisible, hostDetail, hostVMs,
      loadVMs, openVmDetail, vmPower, setCpu, setMemory, addDisk,
      createSnap, revertSnap, deleteSnap,
      batchPower, batchSnap, doBatchSnap,
      openHostDetail, disconnect,
      onMenuSelect, onVmSelection, refresh,
      Math
    };
  }
});

app.use(ElementPlus);
app.mount('#app');