# HOTPOOR FairDeck

**HOTPOOR扑克** · Play together. Verify for yourself.

多玩法扑克项目，优先设计交互、参与者约定与可验证公平。
当前是节点目录、存储 API、Unity 钱包和浏览器原型；尚无完整对局、共识、智能合约或零知识洗牌。

## 工程

- `core/`：协议基础、节点登记与发现工具。
- `server/`：PostgreSQL 三库、多端口 API、七牛归档、OpenResty。
- `apps/unity/HOTPOOR_Poker/`：Unity 6.6 客户端。
- `apps/explorer/`：GitHub Pages 公开记录浏览器。

## 文档

- [架构与当前范围](Docs/ARCHITECTURE.md)
- [游戏机制](Docs/GAME_MECHANICS.md)
- [服务端启动](server/README.md)
- [Unity 钱包与桌前场景](apps/unity/README.md)
- [方案评审](Docs/VERIFIABLE_GAME_REVIEW.md)

API 目标为 https://blockchain.xialiwei.com，配置不代表域名和服务已经部署。
`.secrets`、wallet、实际连接配置、数据库和 Unity 缓存不提交到 Git。
