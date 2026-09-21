# FairDeck 基础服务部署记录

日期：2026-09-21。仅部署基础 API、索引存储和归档，不代表完成共识或棋牌游戏协议。

## 入口

- API：https://blockchain.xialiwei.com
- 健康检查：https://blockchain.xialiwei.com/health
- 浏览器：https://github.xialiwei.com/HOTPOOR-FairDeck/

复用已有域名解析与覆盖该子域名的 TLS 证书。新增独立 Nginx 虚拟主机，现有站点保留。

## 运行布局

| 组件 | 本机监听或位置 |
| --- | --- |
| 现有宿主 Nginx | 80/443，仅为 FairDeck 添加独立虚拟主机 |
| fairdeck-openresty | 127.0.0.1:8890，Lua 访问控制与双上游 |
| fairdeck-api-8811 | 127.0.0.1:8811 |
| fairdeck-api-8812 | 127.0.0.1:8812，独立进程 |
| fairdeck-archive | 后台归档 worker，不监听端口 |
| fairdeck-postgres | 127.0.0.1:15432，独立持久卷 |
| 代码 | /opt/fairdeck/source |
| 依赖 | /opt/fairdeck/dependencies，只读挂载 |
| 实际配置 | /opt/fairdeck/config，权限受限，不进入仓库 |

基础镜像 Python 3.12、PostgreSQL 17、OpenResty 1.27.1.2。当前使用只读源码和依赖挂载，API/worker 为非 root、无 capabilities、只读根目录，并限制内存和 CPU；容器配置自动重启。

主机为旧版 Docker：桥接 DNS/iptables 不可用，使用 host 网络且服务仅绑定 loopback。旧版默认 seccomp 与新镜像存在进程创建兼容问题，本次仅 FairDeck 容器采用与该机既有项目一致的 seccomp=unconfined；这降低了这些容器的系统调用隔离。未修改全局 Docker 配置或其他项目。后续更新运行环境后应恢复默认 seccomp 并重新验证。

## 数据与七牛

数据库为 blockchain、blockchain1、blockchain2；应用角色 fairdeck 独立于初始化管理员。实体库各只有 entities 应用表。

复用用户指定的七牛桶和域名，不修改已有桶公开策略。所有 FairDeck 归档对象先做 AES-256-GCM 封装，包含随机 nonce、认证标签及绑定对象键的附加认证数据；匿名访问只能拿到密文。
API 按实体权限鉴权后取回、解密并验证内容 SHA-256。密码学扑克中的隐藏牌还需独立端到端协议，这一层不能阻止持有归档密钥的服务端解密。

本次新建归档密钥、数据库角色密码和 operator 令牌，已在本机 C/J 两个 .secrets 目录各备份一份。Git、Docker 构建上下文和 Pages 均排除实际凭据。归档密钥是恢复归档的必要材料，不可只备份数据库。

## 线上验证

- 双 API 端口与 Lua 代理健康检查通过。
- Nginx 完整配置校验后热加载；域名 HTTPS 访问正常。
- 真 RSA 钱包签名挑战登录通过，同一挑战重复提交被拒绝。
- operator 外部请求被 Lua 拒绝；私有实体匿名查询被拒绝。
- 七牛真实上传、回读校验、API 授权取回通过。
- 对七牛对象匿名读取，仅得到 FDAR1 加密封装，不含测试明文。
- 临时测试对象、实体与测试身份已清理。

后续仍需长期运行监控、数据库备份恢复演练、会话清理、压测、完整对局与共识协议。
