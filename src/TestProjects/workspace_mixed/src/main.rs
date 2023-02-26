use std;
use shared::models::ModelA;
// use shared::systems::system_a:: { do_stuff_from_system_a }; // or (1)
// use shared::systems::system_a::*; // or this one (1)
use shared::systems::*; // or this one (2)

fn main() {
    let a = shared::A::new(10);
    let model_a = ModelA {
        value: 10,
    };

    println!("52 is equal to {}", a.get_value());
    println!("10 is equal to {}", model_a.value);
    // println!("must be 42 {}", do_stuff_from_system_a()); // (1)
    println!("42 is equal to {}", system_a::do_stuff_from_system_a()); // (2)

    println!("A B = {}", std::env::var_os("A B").unwrap_or(std::ffi::OsString::from("NOT FOUND!")).to_str().unwrap());
    println!("XX = {}", std::env::var("XX").unwrap_or("NOT FOUND!".to_string()));
    println!("A = {}", std::env::var("A").unwrap_or("NOT FOUND!".to_string()));

    let args: Vec<String> = std::env::args().collect();
    println!("command line: {:?}", args);

    let mut user_input = String::new();
    std::io::stdin().read_line(&mut user_input);
    }
