pub mod models;
pub mod systems;
pub struct A2 {
    value: u8,
}

impl A2 {
    pub fn new(value: u8) -> A2 {
        A2 {
            value,
        }
    }

    pub fn get_value(&self) -> u8 {
        let result = systems::system_a::do_stuff_from_system_a();
        self.value + result
    }
}
