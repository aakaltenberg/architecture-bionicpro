Для выполнения 4 задания пришлось вручную загружать ldif-файл, т.к. с windows он не хотел нормально подгружаться сам, и синхронизация падала.
```bash
docker exec -it architecture-bionicpro-openldap-1 bash
ldapadd -x -D "cn=admin,dc=example,dc=com" -w admin123 -f /ldifs/custom/config.ldif
```

После выполнения команды все отработало.

# В keycloak добавлена User Federation:
Параметр	               Значение
Console Display Name	   LDAP Europe
Vendor	Other
Connection URL	           ldap://openldap:389
Enable StartTLS	           OFF
Bind Type	simple
Bind DN	                   cn=admin,dc=example,dc=com
Bind Credential	           admin123
Users DN	               ou=People,dc=example,dc=com
Edit Mode	               READ_ONLY

# Маппинг ролей:
Параметр	                    Значение
Mapper Type                     role-ldap-mapper
Name	                        LDAP Roles
LDAP Roles DN	                ou=Groups,dc=example,dc=com
Role Name LDAP Attribute        cn
Role Object Classes	            groupOfNames
Membership LDAP Attribute       member
Membership Attribute Type       DN
Membership User LDAP Attribute  uid
Mode	                        READ_ONLY
User Roles Retrieve Strategy	LOAD_GROUPS_BY_MEMBER_ATTRIBUTE
Use Realm Roles Mapping	        ON