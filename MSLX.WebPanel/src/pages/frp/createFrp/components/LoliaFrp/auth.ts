import { request } from '@/utils/request';

const API_BASE_URL = '/api/frp/loliafrp';
export const LOLIA_CALLBACK_PATH = '/oauth/callback/lolia';

export interface LoliaUserInfo {
  id: number;
  username: string;
  avatar: string;
  email: string;
  role: string;
  bandwidth_limit: number;
  max_tunnel_count: number;
  traffic_limit: number;
  traffic_used: number;
  is_banned?: boolean;
  is_baned?: boolean;
}

export interface LoliaNode {
  id: number;
  name: string;
  region_code: string;
  status: string;
  supported_protocols: string[];
  need_kyc: boolean;
  beian_required: boolean;
  bandwidth: number;
  remark: string;
  load: number;
}

export interface LoliaTunnel {
  id: number;
  name: string;
  remark: string;
  type: string;
  status: string;
  node_id: number;
  node_name: string;
  node_address: string;
  local_ip: string;
  local_port: number;
  remote_port: number;
  custom_domain: string;
}

export interface CreateLoliaTunnelParams {
  node_id: number;
  type: string;
  local_ip: string;
  local_port: number;
  remote_port: number;
  custom_domain: string;
  remark: string;
}

export interface LoliaTunnelConfig {
  config: string;
  format: 'toml' | 'yaml' | 'ini';
  tunnel_name: string;
  tunnel_id: number;
  node_name: string;
  remark: string;
}

function apiData<T>(response: any): T {
  if (response?.code !== undefined && response.code !== 200) {
    throw new Error(response.msg || response.message || '请求失败');
  }
  return (response?.data ?? response) as T;
}

export async function getLoliaStatus() {
  const response = await request.get({ url: `${API_BASE_URL}/status` });
  return apiData<LoliaUserInfo | null>(response);
}

export async function getLoliaAuthorizeUrl(redirectUri = `${window.location.origin}${LOLIA_CALLBACK_PATH}`) {
  const response = await request.get({
    url: `${API_BASE_URL}/oauth/url?redirectUri=${encodeURIComponent(redirectUri)}`,
  });
  return apiData<{ url: string; state: string }>(response);
}

export async function completeLoliaAuthorize(code: string, state: string) {
  const response = await request.post({
    url: `${API_BASE_URL}/oauth/callback`,
    data: { code, state },
  });
  return apiData<LoliaUserInfo>(response);
}

export async function logoutLolia() {
  await request.post({ url: `${API_BASE_URL}/logout` });
}

export async function fetchLoliaNodes() {
  const response = await request.post({
    url: `${API_BASE_URL}/nodes`,
    data: { page: 1, limit: 1000 },
  });
  const data = apiData<unknown>(response);
  if (Array.isArray(data)) return data as LoliaNode[];
  const obj = data as Record<string, unknown>;
  const list = (obj?.nodes || obj?.list || obj?.items || obj?.data) as LoliaNode[] | undefined;
  return Array.isArray(list) ? list : [];
}

function normalizeTunnel(raw: Record<string, unknown>): LoliaTunnel {
  return {
    ...raw,
    id: (raw.id ?? raw.tunnel_id ?? 0) as number,
    name: (raw.name ?? raw.tunnel_name ?? '') as string,
    remark: (raw.remark ?? '') as string,
    type: (raw.type ?? 'tcp') as string,
    status: (raw.status ?? '') as string,
    node_id: (raw.node_id ?? 0) as number,
    node_name: (raw.node_name ?? '') as string,
    node_address: (raw.node_address ?? '') as string,
    local_ip: (raw.local_ip ?? '127.0.0.1') as string,
    local_port: (raw.local_port ?? 0) as number,
    remote_port: (raw.remote_port ?? 0) as number,
    custom_domain: (raw.custom_domain ?? '') as string,
  } as LoliaTunnel;
}

export async function fetchLoliaTunnels() {
  const response = await request.get({
    url: `${API_BASE_URL}/tunnels?page=1&limit=1000`,
  });
  const data = apiData<unknown>(response);
  if (Array.isArray(data)) return data.map((item) => normalizeTunnel(item as Record<string, unknown>));
  const obj = data as Record<string, unknown>;
  const list = (obj?.list || obj?.tunnels || obj?.items || obj?.data) as LoliaTunnel[] | undefined;
  return Array.isArray(list) ? list.map((item) => normalizeTunnel(item as unknown as Record<string, unknown>)) : [];
}

export async function createLoliaTunnel(params: CreateLoliaTunnelParams) {
  const response = await request.post({
    url: `${API_BASE_URL}/tunnels`,
    data: params,
  });
  return apiData<LoliaTunnel | null>(response);
}

export async function deleteLoliaTunnel(name: string) {
  const response = await request.delete({ url: `${API_BASE_URL}/tunnels/${encodeURIComponent(name)}` });
  return apiData<unknown>(response);
}

export async function fetchLoliaTunnelConfig(name: string) {
  const response = await request.get({
    url: `${API_BASE_URL}/tunnel-config?name=${encodeURIComponent(name)}`,
  });
  const data = apiData<LoliaTunnelConfig>(response);
  if (!data?.config) throw new Error('Lolia 配置内容为空');
  return data;
}
