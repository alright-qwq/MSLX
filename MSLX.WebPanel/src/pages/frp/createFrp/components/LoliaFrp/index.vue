<script setup lang="ts">
import { UserCircleIcon, ServerIcon, CloudIcon, AddIcon, PlayCircleIcon, RefreshIcon } from 'tdesign-icons-vue-next';
import { changeUrl } from '@/router';
import { openLoginPopup } from '@/utils/popup';
import { onBeforeUnmount, onMounted, ref, computed } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { createFrpTunnel } from '@/pages/frp/createFrp/utils/create';
import CreateTunnelDialog from './components/CreateTunnelDialog.vue';
import {
  deleteLoliaTunnel,
  fetchLoliaNodes,
  fetchLoliaTunnelConfig,
  fetchLoliaTunnels,
  getLoliaAuthorizeUrl,
  getLoliaStatus,
  logoutLolia,
  type LoliaNode,
  type LoliaTunnel,
  type LoliaUserInfo,
} from './auth';

const showCreateDialog = ref(false);
const popupWindow = ref<Window | null>(null);
const loading = ref(false);
const userInfo = ref<LoliaUserInfo | null>(null);
const nodes = ref<LoliaNode[]>([]);
const tunnels = ref<LoliaTunnel[]>([]);
const selectedTunnelName = ref<string | null>(null);

const currentTunnel = computed(() => {
  return tunnels.value.find((tunnel) => tunnel.name === selectedTunnelName.value) || null;
});

const hasLoliaAuth = computed(() => Boolean(userInfo.value));

const remoteAddress = computed(() => {
  const tunnel = currentTunnel.value;
  if (!tunnel) return '';
  return tunnel.custom_domain || `${tunnel.node_address}:${tunnel.remote_port}`;
});

function isTunnelOnline(status?: string) {
  return ['active', 'online', 'running', 'connected'].includes((status || '').toLowerCase());
}

function formatTraffic(bytes: number) {
  return `${(Math.max(0, bytes) / 1073741824).toFixed(2)} GB`;
}

function formatBandwidth(limit?: number) {
  if (!Number.isFinite(limit) || (limit || 0) <= 0) return '未知';
  return `${limit * 8} Mbps`;
}

const handleCreateSuccess = () => {
  void initDashboardData();
};

onMounted(() => {
  void checkAuth();
  window.addEventListener('storage', onOAuthSuccess);
});

onBeforeUnmount(() => {
  window.removeEventListener('storage', onOAuthSuccess);
  if (popupWindow.value && !popupWindow.value.closed) {
    popupWindow.value.close();
  }
});

function onOAuthSuccess(event: StorageEvent) {
  if (event.key === 'lolia_oauth_success' && event.newValue) {
    window.localStorage.removeItem('lolia_oauth_success');
    void checkAuth().then(() => MessagePlugin.success('Lolia FRP 授权登录成功'));
  }
}

async function checkAuth() {
  try {
    userInfo.value = await getLoliaStatus();
    if (userInfo.value) {
      await initDashboardData(false);
    }
  } catch {
    userInfo.value = null;
  }
}

async function connect() {
  try {
    const redirectUri = `${window.location.origin}/oauth/callback/lolia`;
    const { url } = await getLoliaAuthorizeUrl(redirectUri);
    popupWindow.value = openLoginPopup(url, 'Lolia FRP 授权', 620, 700);
  } catch (error: any) {
    MessagePlugin.error(error?.response?.data?.msg || error?.message || '启动授权失败');
  }
}

async function initDashboardData(showFailureMessage = true) {
  loading.value = true;
  try {
    const [nextUserInfo, nextNodes, nextTunnels] = await Promise.all([
      getLoliaStatus(),
      fetchLoliaNodes(),
      fetchLoliaTunnels(),
    ]);

    userInfo.value = nextUserInfo;
    nodes.value = nextNodes;
    tunnels.value = nextTunnels || [];

    if (tunnels.value.length === 0) {
      selectedTunnelName.value = null;
    } else if (!tunnels.value.some((item) => item.name === selectedTunnelName.value)) {
      selectedTunnelName.value = tunnels.value[0].name;
    }

    return true;
  } catch (error: any) {
    const errorMsg = error?.response?.data?.msg || error?.message || '授权已失效或网络异常';
    if (showFailureMessage) {
      MessagePlugin.error(`Lolia FRP 数据加载失败：${errorMsg}`);
    }
    return false;
  } finally {
    loading.value = false;
  }
}

const isAddingTunnel = ref(false);

async function handleUseTunnel() {
  if (!currentTunnel.value) return;
  isAddingTunnel.value = true;

  try {
    const configData = await fetchLoliaTunnelConfig(currentTunnel.value.name);
    const displayName = `${currentTunnel.value.remark || currentTunnel.value.name} | ${currentTunnel.value.node_name}`;
    await createFrpTunnel(displayName, configData.config, 'Lolia Frp', configData.format);
    MessagePlugin.success('配置文件已成功加载');
  } catch (error: any) {
    const errorMsg = error?.response?.data?.msg || error?.message || '未知错误';
    MessagePlugin.error(`获取配置异常: ${errorMsg}`);
  } finally {
    isAddingTunnel.value = false;
  }
}

async function disconnect() {
  await logoutLolia();
  userInfo.value = null;
  nodes.value = [];
  tunnels.value = [];
  selectedTunnelName.value = null;
  MessagePlugin.success('已断开 Lolia FRP 授权');
}

async function refresh() {
  const loaded = await initDashboardData();
  if (loaded) {
    MessagePlugin.success('数据已更新');
  }
}

const isDeleting = ref(false);

async function handleDeleteTunnel() {
  if (!currentTunnel.value) return;
  isDeleting.value = true;

  try {
    await deleteLoliaTunnel(currentTunnel.value.name);
    MessagePlugin.success('隧道删除成功');
    selectedTunnelName.value = null;
    await initDashboardData();
  } catch (error: any) {
    const errorMsg = error?.response?.data?.msg || error?.message || '未知错误';
    MessagePlugin.error(`删除失败: ${errorMsg}`);
  } finally {
    isDeleting.value = false;
  }
}
</script>

<template>
  <div class="mx-auto pb-6 text-[var(--td-text-color-primary)]">
    <div v-if="!hasLoliaAuth" class="flex items-center justify-center min-h-[70vh] list-item-anim">
      <div class="design-card relative w-full max-w-md bg-[var(--td-bg-color-container)]/80 rounded-3xl border border-[var(--td-component-border)] shadow-xl p-10 text-center overflow-hidden">
        <div class="absolute -top-20 -right-20 w-60 h-60 bg-[var(--color-primary)]/10 rounded-full blur-3xl pointer-events-none"></div>
        <div class="absolute -bottom-10 -left-10 w-40 h-40 bg-[var(--color-primary)]/10 rounded-full blur-3xl pointer-events-none"></div>
        <div class="relative z-10 flex flex-col items-center">
          <div class="w-20 h-20 bg-[var(--color-primary)]/10 rounded-2xl flex items-center justify-center mb-6 shadow-sm border border-[var(--color-primary)]/20 overflow-hidden p-2">
            <cloud-icon size="36px" class="text-[var(--color-primary)]" />
          </div>
          <h2 class="text-2xl font-extrabold text-[var(--td-text-color-primary)] !mb-2 tracking-tight">连接 Lolia FRP</h2>
          <p class="text-sm text-[var(--td-text-color-secondary)] !mb-6 font-medium">
            使用浏览器完成官方 OAuth2 授权，MSLX 会自动同步您的 Lolia FRP 账户
          </p>
          <div class="w-full">
            <t-button block theme="primary" size="large" class="!rounded-xl !h-12 !font-bold shadow-md shadow-[var(--color-primary-light)]/30 hover:shadow-[var(--color-primary-light)]/50" @click="connect">
              <template #icon><user-circle-icon /></template>
              授权登录
            </t-button>
          </div>
          <div class="mt-6 pt-4 border-t border-dashed border-zinc-200 dark:border-zinc-700 w-full">
            <t-button variant="text" size="small" class="text-zinc-500 hover:text-[var(--color-primary)]" @click="changeUrl('https://dash.lolia.link')">Lolia FRP 控制台</t-button>
          </div>
        </div>
      </div>
    </div>

    <div v-else id="app-space" class="relative flex flex-col gap-6">
      <t-loading attach="#app-space" :loading="loading" text="加载数据中..." />

      <div v-if="userInfo" class="design-card list-item-anim bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm p-5 sm:p-6" style="animation-delay: 0s">
        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60">
          <div class="flex items-center gap-3">
            <t-avatar :image="userInfo.avatar" size="medium" shape="round" />
            <div class="flex flex-col">
              <h3 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none">Lolia FRP 账户</h3>
              <span class="text-xs text-zinc-500 mt-1">{{ userInfo.email }}</span>
            </div>
          </div>
          <div class="flex items-center gap-2">
            <t-tag v-if="userInfo.is_banned || userInfo.is_baned" theme="danger" variant="light-outline" class="!rounded-md !font-bold">已封禁</t-tag>
            <t-tag v-if="userInfo.role" theme="primary" variant="light-outline" class="!rounded-md !font-bold">{{ userInfo.role }}</t-tag>
            <div class="w-px h-4 bg-zinc-200 dark:bg-zinc-700 mx-1"></div>
            <t-popconfirm content="确认断开 Lolia FRP 的连接吗？" @confirm="disconnect">
              <t-button variant="text" theme="danger" size="small" class="!rounded-lg hover:!bg-red-500/10">退出登录</t-button>
            </t-popconfirm>
          </div>
        </div>

        <div class="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1">用户名称</div>
            <div class="text-lg font-bold text-[var(--td-text-color-primary)] truncate">{{ userInfo.username }}</div>
          </div>
          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1">隧道配额</div>
            <div class="text-lg font-bold text-[var(--td-text-color-primary)] font-mono">
              <span class="text-[var(--color-primary)]">{{ tunnels.length }}</span> / {{ userInfo.max_tunnel_count }}
              <span class="text-sm font-medium text-zinc-500">条</span>
            </div>
          </div>
          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1">带宽限制</div>
            <div class="text-lg font-bold text-[var(--td-text-color-primary)] font-mono">{{ formatBandwidth(userInfo.bandwidth_limit) }}</div>
          </div>
          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1">剩余流量</div>
            <div class="text-[15px] font-bold text-[var(--color-success)] font-mono mt-0.5">{{ formatTraffic((userInfo.traffic_limit || 0) - (userInfo.traffic_used || 0)) }}</div>
          </div>
        </div>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        <div class="lg:col-span-5 xl:col-span-4 design-card list-item-anim flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm h-[580px]" style="animation-delay: 0.1s">
          <div class="flex items-center justify-between p-4 sm:p-5 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 shrink-0">
            <h3 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">我的隧道</h3>
            <div class="flex items-center gap-1">
              <t-button size="small" variant="text" class="!px-2 hover:!bg-zinc-100 dark:hover:!bg-zinc-700/50" :loading="loading" @click="refresh">
                <template #icon><refresh-icon /></template>刷新
              </t-button>
              <t-button size="small" theme="primary" class="!px-3 !ml-1 !rounded-lg" @click="showCreateDialog = true">
                <template #icon><add-icon /></template>新建
              </t-button>
            </div>
          </div>

          <div class="flex-1 overflow-y-auto custom-scrollbar p-3">
            <div v-if="tunnels.length > 0" class="flex flex-col gap-2">
              <div v-for="tunnel in tunnels" :key="tunnel.name || tunnel.id" class="group flex items-center p-3 rounded-xl cursor-pointer transition-all duration-300 border" :class="selectedTunnelName === tunnel.name ? 'bg-[var(--color-primary)]/10 border-[var(--color-primary)]/30 shadow-sm' : 'bg-transparent border-transparent hover:bg-zinc-50 dark:hover:bg-zinc-700/50 hover:border-zinc-200 dark:hover:border-zinc-600'" @click="selectedTunnelName = tunnel.name">
                <div class="w-10 h-10 rounded-lg flex items-center justify-center shrink-0 mr-3 transition-colors" :class="selectedTunnelName === tunnel.name ? 'bg-[var(--color-primary)] text-white shadow-md shadow-[var(--color-primary)]/30' : 'bg-zinc-100 dark:bg-zinc-900 text-[var(--td-text-color-secondary)] group-hover:text-zinc-800 dark:group-hover:text-zinc-200'">
                  <server-icon size="20px" />
                </div>
                <div class="flex-1 min-w-0 mr-3">
                  <div class="font-bold text-sm truncate transition-colors" :class="selectedTunnelName === tunnel.name ? 'text-[var(--color-primary)]' : 'text-[var(--td-text-color-primary)]'">{{ tunnel.remark || tunnel.name }}</div>
                  <div class="text-[11px] text-[var(--td-text-color-secondary)] truncate mt-0.5">{{ tunnel.node_name }} · {{ tunnel.type.toUpperCase() }}</div>
                </div>
                <div class="shrink-0">
                  <t-tag v-if="isTunnelOnline(tunnel.status)" theme="success" variant="light" size="small" class="!rounded !font-bold !px-1.5">在线</t-tag>
                  <t-tag v-else theme="default" variant="light" size="small" class="!rounded !font-bold !px-1.5 !text-zinc-500">离线</t-tag>
                </div>
              </div>
            </div>
            <div v-else class="h-full flex flex-col items-center justify-center opacity-60">
              <server-icon size="32px" class="text-zinc-400 mb-2" />
              <span class="text-sm text-zinc-500 font-medium">暂无隧道，请先新建</span>
            </div>
          </div>
        </div>

        <div class="lg:col-span-7 xl:col-span-8 design-card list-item-anim flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm h-[580px]" style="animation-delay: 0.2s">
          <template v-if="currentTunnel">
            <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 sm:p-6 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 shrink-0">
              <div class="flex flex-col min-w-0">
                <h3 class="text-xl font-extrabold text-[var(--td-text-color-primary)] m-0 truncate">{{ currentTunnel.remark || currentTunnel.name }}</h3>
                <p class="text-xs text-[var(--td-text-color-secondary)] mt-1 truncate font-mono bg-zinc-100 dark:bg-zinc-800/50 w-max px-2 py-0.5 rounded">ID: {{ currentTunnel.id }}</p>
              </div>
              <div class="shrink-0">
                <t-popconfirm content="确认删除此隧道吗？将无法恢复！" theme="danger" placement="bottom-right" @confirm="handleDeleteTunnel">
                  <t-button theme="danger" class="!rounded-lg hover:!bg-red-500 hover:!text-white transition-colors" :loading="isDeleting">
                    <template #icon><t-icon name="delete" /></template>
                    删除隧道
                  </t-button>
                </t-popconfirm>
              </div>
            </div>

            <div class="flex-1 overflow-y-auto custom-scrollbar p-5 sm:p-6">
              <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-2 gap-4">
                <div class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-[var(--td-component-border)] flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1.5">所在节点</span>
                  <span class="text-sm font-bold text-[var(--td-text-color-primary)] truncate" :title="currentTunnel.node_name">{{ currentTunnel.node_name }}</span>
                </div>
                <div class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-[var(--td-component-border)] flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1.5">本地地址</span>
                  <span class="text-sm font-mono font-bold text-[var(--td-text-color-primary)]">{{ currentTunnel.local_ip }}:{{ currentTunnel.local_port }}</span>
                </div>
                <div class="p-4 bg-emerald-50/50 dark:bg-emerald-900/20 rounded-xl border border-emerald-200/50 dark:border-emerald-800/30 flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-emerald-600/80 dark:text-emerald-500/80 uppercase tracking-widest mb-1.5">远程地址</span>
                  <span class="text-lg font-mono font-extrabold text-emerald-600 dark:text-emerald-400 truncate">{{ remoteAddress }}</span>
                </div>
                <div class="p-4 rounded-xl flex flex-col justify-center border transition-colors" :class="isTunnelOnline(currentTunnel.status) ? 'bg-emerald-50/50 dark:bg-emerald-900/10 border-emerald-200/50 dark:border-emerald-800/30' : 'bg-zinc-50/80 dark:bg-zinc-900/50 border-[var(--td-component-border)]'">
                  <span class="text-[11px] font-extrabold uppercase tracking-widest mb-1.5" :class="isTunnelOnline(currentTunnel.status) ? 'text-emerald-600/80 dark:text-emerald-500/80' : 'text-[var(--td-text-color-secondary)]'">当前状态</span>
                  <div class="flex items-center gap-2">
                    <span v-if="isTunnelOnline(currentTunnel.status)" class="w-2 h-2 rounded-full bg-[var(--color-success)] animate-pulse"></span>
                    <span class="text-sm font-bold" :class="isTunnelOnline(currentTunnel.status) ? 'text-[var(--color-success)]' : 'text-zinc-500'">{{ isTunnelOnline(currentTunnel.status) ? '节点在线' : '离线' }}</span>
                  </div>
                </div>
              </div>
              <div class="mt-8">
                <t-button theme="primary" size="large" :loading="isAddingTunnel" block class="!rounded-xl !h-12 !font-bold shadow-md shadow-[var(--color-primary-light)]/40 hover:shadow-[var(--color-primary-light)]/60 transition-shadow text-base" @click="handleUseTunnel">
                  <template #icon><play-circle-icon /></template>
                  使用此隧道
                </t-button>
              </div>
            </div>
          </template>
          <template v-else>
            <div class="flex-1 flex flex-col items-center justify-center opacity-50 p-6 text-center">
              <div class="w-24 h-24 bg-zinc-100 dark:bg-zinc-800 rounded-full flex items-center justify-center mb-4"><cloud-icon size="40px" class="text-zinc-400" /></div>
              <h3 class="text-base font-bold text-zinc-700 dark:text-zinc-300 mb-1">未选择隧道</h3>
              <p class="text-sm text-zinc-500">请在左侧列表中选择一个隧道以查看详细信息</p>
            </div>
          </template>
        </div>
      </div>
    </div>

    <create-tunnel-dialog v-if="showCreateDialog" v-model:visible="showCreateDialog" :nodes="nodes" @success="handleCreateSuccess" />
  </div>
</template>

<style scoped lang="less">
@import '@/style/scrollbar';

.list-item-anim {
  animation: slideUp 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
}

@keyframes slideUp {
  from { opacity: 0; transform: translateY(20px); }
  to { opacity: 1; transform: translateY(0); }
}

@keyframes smoothLoadingGlass {
  from { backdrop-filter: blur(0.01px) !important; -webkit-backdrop-filter: blur(0.01px) !important; }
  to { backdrop-filter: blur(4px) !important; -webkit-backdrop-filter: blur(4px) !important; }
}

.custom-scrollbar {
  .scrollbar-mixin();
}

:deep(.t-loading__overlay) {
  border-radius: 1rem !important;
  background: rgb(255 255 255 / 50%) !important;
  animation: smoothLoadingGlass 0.3s cubic-bezier(0.2, 0.8, 0.2, 1) forwards !important;
}

:global(.dark) :deep(.t-loading__overlay) {
  background: rgb(24 24 27 / 50%) !important;
}
</style>
