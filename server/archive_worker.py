import time
from server.db import Store, config
from server.storage import Cloud

if __name__ == "__main__":
    cfg = config()
    store, cloud = Store(cfg), Cloud(cfg["storage"])
    while True:
        try:
            if not store.archive_one(cloud):
                time.sleep(2)
        except Exception:
            print("Archive retry pending; original retained.", flush=True)
            time.sleep(10)
