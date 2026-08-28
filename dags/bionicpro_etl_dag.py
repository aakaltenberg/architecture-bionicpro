from airflow import DAG
from airflow.operators.python import PythonOperator
from datetime import datetime, timedelta
import clickhouse_connect
import csv
import random

default_args = {
    'owner': 'bionicpro',
    'start_date': datetime(2025, 1, 1),
    'retries': 1,
    'retry_delay': timedelta(minutes=5),
}

def generate_telemetry(**context):
    execution_date = context['execution_date']
    output_path = f'/opt/airflow/sample_files/telemetry_{execution_date.strftime("%Y%m%d")}.csv'
    users = ['cd63f90c-fd21-45bd-836c-95c24b9ba5c2', 'user2', 'user3']
    movements = ['walk', 'stand', 'sit', 'error', 'grasp']
    
    with open(output_path, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(['user_id', 'timestamp', 'signal_value', 'movement_type'])
        base_time = execution_date - timedelta(days=1)
        for i in range(100):
            user = random.choice(users)
            timestamp = base_time + timedelta(minutes=i*5)
            signal = random.randint(50, 100)
            movement = random.choice(movements)
            writer.writerow([user, timestamp.isoformat(), signal, movement])
    
    context['ti'].xcom_push(key='telemetry_file', value=output_path)

def generate_crm(**context):
    crm_data = [
        {'id': 'cd63f90c-fd21-45bd-836c-95c24b9ba5c2', 'name': 'User One'},
        {'id': 'c4558588-0206-4b45-837c-61dd58f764aa', 'name': 'User0'},
    ]
    context['ti'].xcom_push(key='crm_data', value=crm_data)

def transform_and_load(**context):
    ti = context['ti']
    telemetry_file = ti.xcom_pull(task_ids='generate_telemetry', key='telemetry_file')
    crm_data = ti.xcom_pull(task_ids='generate_crm', key='crm_data')
    
    # Агрегация
    user_stats = {}
    with open(telemetry_file, 'r') as f:
        reader = csv.DictReader(f)
        for row in reader:
            uid = row['user_id']
            if uid not in user_stats:
                user_stats[uid] = {'total_movements': 0, 'errors': 0}
            user_stats[uid]['total_movements'] += 1
            if row['movement_type'] == 'error':
                user_stats[uid]['errors'] += 1
    
    # Подключение к ClickHouse
    client = clickhouse_connect.get_client(
        host='clickhouse',
        port=8123,
        database='reports',
        username='default',
        password='default_psw'
    )
    
    execution_date = context['execution_date'].date()
    data_to_insert = []
    for uid, stats in user_stats.items():
        #crm_info = next((u for u in crm_data if u['id'] == uid), {})
        data_to_insert.append([
            execution_date,
            uid,
            #crm_info.get('name', ''),
            stats['total_movements'],
            stats['errors']
        ])
    
    # if data_to_insert:
    #     client.insert('report_view', data_to_insert)
    if data_to_insert:
        client.insert('reports.telemetry_final', data_to_insert)

with DAG(
    dag_id='bionicpro_etl_emulated',
    default_args=default_args,
    schedule_interval='@daily',
    catchup=False,
    description='ETL with emulated telemetry'
) as dag:

    generate_telemetry_task = PythonOperator(
        task_id='generate_telemetry',
        python_callable=generate_telemetry
    )
    
    generate_crm_task = PythonOperator(
        task_id='generate_crm',
        python_callable=generate_crm
    )
    
    transform_load_task = PythonOperator(
        task_id='transform_and_load',
        python_callable=transform_and_load
    )
    
    [generate_telemetry_task, generate_crm_task] >> transform_load_task