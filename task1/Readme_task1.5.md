1. Создал копию flow от browser, с именем Browser with OTP
2. В созданном flow нажал Add step и выберал OTP Form. Перетащил его так, чтобы он следовал сразу после Username Password Form. Состояние проставил REQUIRED
3. В меню слева выберал Clients → bionicpro-auth. В разделе Advanced переопределил Browser Flow -> Browser with OTP Flow
4. В меню слева выберал Authentication → вкладка Required Actions.
5. Для действия Configure OTP проверил, что оно Enabled, а также проставил галочку Default Action. Это заставит каждого пользователя, у которого ещё не настроен OTP, пройти его настройку при следующем входе.
Для уже существующих пользователей OTP будет предложен при первом же входе после включения этой настройки.
