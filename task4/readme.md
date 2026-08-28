## Регистрация коннектора
После запуска контейнеров необходимо выполнить регистрацию коннектора:

```sh
curl -X POST -H "Content-Type: application/json" --data '{
  "name": "crm-connector",
  "config": {
    "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
    "database.hostname": "crm-db",
    "database.port": "5432",
    "database.user": "crm_user",
    "database.password": "crm_password",
    "database.dbname": "crm",
    "database.server.name": "crm_server",
    "table.include.list": "public.crm_users",
    "topic.prefix": "crm_cdc",
    "plugin.name": "pgoutput",
    "slot.name": "debezium_crm",
    "publication.autocreate.mode": "filtered"
  }
}' http://localhost:8083/connectors
```

## Проверка:
1. Подключаемся к crm-db:
```sh
docker exec -it architecture-bionicpro-crm-db-1 psql -U crm_user -d crm
```
2. Подключаемся к crm-db:
```sql
UPDATE crm_users SET full_name = 'Иван Петров (обновлено)' WHERE id = 'cd63f90c-fd21-45bd-836c-95c24b9ba5c2';
```

3. Проверка, что сообщение попало в кафку:
```sh
docker exec -it architecture-bionicpro-kafka-1 kafka-console-consumer --bootstrap-server kafka:9092 --topic crm_cdc.public.crm_users --from-beginning
```

PS C:\architecture-bionicpro> docker exec -it architecture-bionicpro-kafka-1 kafka-console-consumer --bootstrap-server kafka:9092 --topic crm_cdc.public.crm_users --from-beginning
{"before":null,"after":{"id":"cd63f90c-fd21-45bd-836c-95c24b9ba5c2","full_name":"Иван Петров","email":"ivan@example.com","phone":"+79991234567","created_at":1786576151334036},"source":{"version":"2.4.0.Final","connector":"postgresql","name":"crm_cdc","ts_ms":1786576549339,"snapshot":"first","db":"crm","sequence":"[null,\"26324528\"]","schema":"public","table":"crm_users","txId":735,"lsn":26324528,"xmin":null},"op":"r","ts_ms":1786576550997,"transaction":null}
{"before":null,"after":{"id":"c4558588-0206-4b45-837c-61dd58f764aa","full_name":"Мария Смирнова","email":"maria@example.com","phone":"+79997654321","created_at":1786576151334036},"source":{"version":"2.4.0.Final","connector":"postgresql","name":"crm_cdc","ts_ms":1786576549339,"snapshot":"last","db":"crm","sequence":"[null,\"26324528\"]","schema":"public","table":"crm_users","txId":735,"lsn":26324528,"xmin":null},"op":"r","ts_ms":1786576551024,"transaction":null}
{"before":null,"after":{"id":"cd63f90c-fd21-45bd-836c-95c24b9ba5c2","full_name":"Иван Петров (обновлено)","email":"ivan@example.com","phone":"+79991234567","created_at":1786576151334036},"source":{"version":"2.4.0.Final","connector":"postgresql","name":"crm_cdc","ts_ms":1786576666065,"snapshot":"false","db":"crm","sequence":"[null,\"26324624\"]","schema":"public","table":"crm_users","txId":736,"lsn":26324624,"xmin":null},"op":"u","ts_ms":1786576670262,"transaction":null}

4. Проверка, что данные появились в clickhouse:
```sh
docker exec -it 564b56d688cddcd8db7bb7dd3310bad0c7f2d0292758e45baab020d24f8e5555 clickhouse-client
```

```sql
USE reports;
SELECT *
FROM reports.crm_users_final
```
   ┌─id───────────────────────────────────┬─full_name───────────────┬─email────────────┬─phone────────┐
1. │ cd63f90c-fd21-45bd-836c-95c24b9ba5c2 │ Иван Петров (обновлено) │ ivan@example.com │ +79991234567 │
   └──────────────────────────────────────┴─────────────────────────┴──────────────────┴──────────────┘

Проверка данных в новой витрине
```sql
SELECT *
FROM reports.report_view_v2
```

   ┌───────date─┬─user_id──────────────────────────────┬─user_name───────────────┬─total_movements─┬─errors─┐
1. │ 2026-08-12 │ cd63f90c-fd21-45bd-836c-95c24b9ba5c2 │ Иван Петров (обновлено) │              32 │      8 │
2. │ 2026-08-12 │ user2                                │                         │              37 │      9 │
3. │ 2026-08-12 │ user3                                │                         │              31 │      8 │
4. │ 2026-08-12 │ cd63f90c-fd21-45bd-836c-95c24b9ba5c2 │ Иван Петров (обновлено) │              32 │      8 │
5. │ 2026-08-12 │ user2                                │                         │              37 │      9 │
6. │ 2026-08-12 │ user3                                │                         │              31 │      8 │
7. │ 2026-08-12 │ cd63f90c-fd21-45bd-836c-95c24b9ba5c2 │ Иван Петров (обновлено) │              32 │      8 │
8. │ 2026-08-12 │ user2                                │                         │              37 │      9 │
9. │ 2026-08-12 │ user3                                │                         │              31 │      8 │
   └────────────┴──────────────────────────────────────┴─────────────────────────┴─────────────────┴────────┘


## Ручной запуск dag:
```sh
docker exec -it architecture-bionicpro-airflow-scheduler-1 airflow tasks test bionicpro_etl_emulated transform_and_load 2026-08-12
```
