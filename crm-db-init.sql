CREATE TABLE crm_users (
    id UUID PRIMARY KEY,
    full_name VARCHAR(255) NOT NULL,
    email VARCHAR(255) NOT NULL,
    phone VARCHAR(50),
    created_at TIMESTAMP DEFAULT now()
);

INSERT INTO crm_users (id, full_name, email, phone) VALUES
('cd63f90c-fd21-45bd-836c-95c24b9ba5c2', 'Иван Петров', 'ivan@example.com', '+79991234567'),
('c4558588-0206-4b45-837c-61dd58f764aa', 'Мария Смирнова', 'maria@example.com', '+79997654321');