# Server

在仓库根目录运行。依赖 Python 3.10+、PostgreSQL、生产环境 OpenResty 和七牛私有桶。

```powershell
python -m venv .venv
.venv\Scripts\python -m pip install -r server/requirements.txt
Copy-Item server/config.example.json server/config.local.json
```

以数据库管理员创建 `blockchain`、`blockchain1`、`blockchain2`，给专用应用角色授权。连接串与凭据通过环境变量注入，不粘贴到终端历史或仓库。

| 环境变量 | 作用 |
| --- | --- |
| FAIRDECK_INDEX_DSN | blockchain 连接串 |
| FAIRDECK_SHARD1_DSN / FAIRDECK_SHARD2_DSN | 实体分库连接串 |
| FAIRDECK_ADMIN_TOKEN | 高熵本地 operator 令牌 |
| QINIU_ACCESS_KEY / QINIU_SECRET_KEY | 服务端七牛凭据 |
| FAIRDECK_ARCHIVE_KEY | 32 字节随机密钥的 Base64，用于 AES-GCM 归档封装，必须独立备份 |

在 config.local.json 填私有桶 bucket 和 HTTPS download_base。没有云配置时，可测试 `storage: {"driver":"local","directory":"server/.local/objects"}`，它不代表云归档完成。

```powershell
.venv\Scripts\python -m server.db
.venv\Scripts\python -m server.app
# 另开终端
.venv\Scripts\python -m server.archive_worker
```

ports 默认 `[8811,8812]`，同一进程开双监听器便于测试。生产可用每个仅含单端口的独立配置启动多个进程，做到隔离和滚动维护。`FAIRDECK_CONFIG` 可指定实际配置路径。
标准库 HTTP API 是开发原型，正式运行前还需连接数限制、监控和负载测试；保持绑定 127.0.0.1，外部经 OpenResty。

## API

| 方法与路径 | 作用 |
| --- | --- |
| GET /health | 网络与服务状态，明确无共识 |
| POST /v1/wallets/challenge | address、modulus、exponent 换挑战 |
| POST /v1/wallets/verify | challenge_id、signature 换会话 |
| POST /v1/nodes/heartbeat | 会话认证；ip、port、public 更新租约 |
| GET /v1/nodes/me | 本人的节点登记 |
| GET /v1/nodes | 有效公开节点，最多 200 条 |
| GET /v1/games/{32位UUID}/peers | 本局成员专用节点列表 |
| GET /v1/entities/{32位UUID} | public 公开；private 仅 owner 读取 |
| POST /v1/operator/entities | 本机管理员写入 block_id、owner、visibility、body |
| POST /v1/operator/entities/{32位UUID}/lock | 本机管理员排队归档 |

owner 必须是已登记钱包。OpenResty Lua 阻止 operator 从外部访问；存储锁定不等于终局共识。

## 部署

1. DNS A/AAAA 指向确定的服务器，申请 blockchain.xialiwei.com TLS 证书。
2. 调整 `openresty/nginx.conf` 端口与证书路径，执行 `openresty -t` 后加载。
3. CORS 允许来源为 `https://hotpoor.github.io` 和账号现有 Pages 域名 `https://github.xialiwei.com`，来源不含路径。页面须使用 HTTPS。
4. 只有本机可信反代才设置 trust_loopback_proxy=true，反代覆盖 X-Real-IP。

模板提供 Lua 访问控制、双上游、大小与速率限制，尚未在目标机器验证。生产凭据仅放服务端，不能打包进客户端或 Pages。

当前线上部署与实际验证见 [部署记录](../Docs/DEPLOYMENT.md)。原始模板用于直接终止 TLS 的 OpenResty；现有主机使用 host-nginx.conf → OpenResty → 双 API，避免替换现有站点网关。

七牛对象一律使用 AES-256-GCM 额外封装，再上传。即使复用公开桶，匿名读取也只有密文；密钥缺失或错误则拒绝解密。该层是服务端归档保护，不能代替游戏的端到端加密。服务端仍有能力解开这一层。

## 测试与运维边界

`python -m unittest server.tests.test_system -v`

集成测试仅在设置 FAIRDECK_TEST_DSN、FAIRDECK_TEST_SHARD1、FAIRDECK_TEST_SHARD2 时运行，必须指向隔离测试库。Unity 的 SetupDesk.ValidateWallet 在 Logs 生成无私钥的互验样本；缺少样本时该测试会跳过。
正式运行前还需过期挑战/会话清理、孤立实体核查、备份策略和归档热副本回收。
